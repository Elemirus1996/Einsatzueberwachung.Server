using System.Collections.Concurrent;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Ergebnis von POST /suche: 200 (ausgefuehrt oder doppelt), 409 (Uebergang nicht erlaubt) mit kurzem Text</summary>
public sealed record TeamAppSucheErgebnis(bool Ok, bool Doppelt, string? Fehler = null);

/// <summary>
/// Team steuert die Suche selbst aus der App (#133, docs/APP_API.md Abschnitt 11). Wirkung wie die Knoepfe im Monitor
/// bzw. in /el (gleicher TeamSucheService). Die Einsatzleitung bekommt einen Hinweis und kann jederzeit korrigieren.
/// </summary>
public sealed class TeamAppSuche
{
    public static readonly IReadOnlyList<string> Aktionen = ["starten", "pause", "weiter", "beenden"];

    private readonly IEinsatzService _einsatz;
    private readonly TeamSucheService _suche;
    private readonly IWarningService? _hinweise;
    private readonly IAuditLogService? _audit;
    private readonly ConcurrentDictionary<string, DateTime> _ausgefuehrt = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sperre = new(1, 1);

    public TeamAppSuche(IEinsatzService einsatz, TeamSucheService suche, IWarningService? hinweise = null, IAuditLogService? audit = null)
    {
        _einsatz = einsatz;
        _suche = suche;
        _hinweise = hinweise;
        _audit = audit;
    }

    public async Task<TeamAppSucheErgebnis> AusfuehrenAsync(Team team, string id, string aktion, DateTime nowUtc)
    {
        var schluessel = team.TeamId + "|" + id;
        // Nacheinander: Wiederholungen aus dem Funkloch duerfen nicht gleichzeitig zweimal starten
        await _sperre.WaitAsync();
        try
        {
            foreach (var alt in _ausgefuehrt.Where(p => p.Value < nowUtc.AddHours(-24)).Select(p => p.Key).ToList())
                _ausgefuehrt.TryRemove(alt, out _);
            if (_ausgefuehrt.ContainsKey(schluessel)) return new(true, true);

            var status = AppLage.SucheStatus(team);
            string? fehler = (aktion, status) switch
            {
                ("starten", "laeuft" or "kurzpause") => "Die Suche läuft bereits.",
                ("pause", not "laeuft") => status == "kurzpause" ? "Die Suche ist bereits pausiert." : "Es läuft keine Suche.",
                ("weiter", not "kurzpause") => status == "laeuft" ? "Die Suche läuft bereits." : "Die Suche ist nicht pausiert.",
                ("beenden", not ("laeuft" or "kurzpause")) => "Es läuft keine Suche.",
                _ => null
            };
            if (fehler is not null) return new(false, false, fehler);

            try
            {
                switch (aktion)
                {
                    case "starten":
                    case "weiter":
                        await _suche.StartAsync(team.TeamId); // mit dem zugewiesenen Halsband; nach Kurzpause nahtlos weiter
                        break;
                    case "pause":
                        await _einsatz.PauseTeamTimerAsync(team.TeamId);
                        break;
                    case "beenden":
                        await _suche.StopAsync(team.TeamId);
                        break;
                }
            }
            catch (InvalidOperationException ex)
            {
                return new(false, false, ex.Message); // z. B. Hund laeuft bereits in einem anderen Team
            }

            _ausgefuehrt[schluessel] = nowUtc;
            await MeldenAsync(team, aktion);
            return new(true, false);
        }
        finally
        {
            _sperre.Release();
        }
    }

    public static string Text(string aktion) => aktion switch
    {
        "starten" => "hat die Suche gestartet",
        "pause" => "hat die Suche pausiert",
        "weiter" => "setzt die Suche fort",
        _ => "hat die Suche beendet"
    };

    private async Task MeldenAsync(Team team, string aktion)
    {
        var text = $"{team.TeamName} {Text(aktion)} (App)";
        await _einsatz.AddGlobalNoteWithSourceAsync($"[{team.TeamName}] {Text(aktion)} (App)", team.TeamId, team.TeamName,
            QuelleTeamApp, GlobalNotesEntryType.Manual, QuelleTeamApp);
        _hinweise?.AddWarning(new WarningEntry
        {
            Title = "Team-App",
            Message = text,
            Level = WarningLevel.Info,
            TeamId = team.TeamId,
            Source = WarningRuleDefinition.Sources.TeamAppSuche,
            NavigationUrl = $"/einsatz-monitor#team-{team.TeamId}",
            Timestamp = Ortszeit.Jetzt
        });
        _audit?.Log("Suche gesteuert (Team-App)", text, team.TeamName);
    }

    /// <summary>Quelle der Notiz; /el zeigt dazu einen Hinweis mit Ton</summary>
    public const string QuelleTeamApp = "Team-App";
}

using System.Globalization;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Ergebnis von POST /punkt: Fehler → 400 bzw. 409 (Grenze erreicht)</summary>
public sealed record TeamPunktErgebnis(string? PunktId, bool Doppelt, string? Fehler = null, bool GrenzeErreicht = false);

/// <summary>
/// Navigationspunkte, Variante C (docs/APP_API.md Team-Rolle Abschnitt 13): Die Einsatzleitung schickt einem Team einen
/// Kartenpunkt als Ziel (Mitteilung mit Navi-Links), das Team setzt an seinem Standort einen Punkt (Kartenpunkt und Notiz).
/// </summary>
public sealed class Navigationspunkte
{
    public const int MaxPunkteJeTeam = 50;
    public const int MaxTextLaenge = 200;
    /// <summary>Farbe der Punkte, die ein Team per App setzt (unterscheidbar von den blauen Punkten der Einsatzleitung)</summary>
    public const string FarbeTeamPunkt = "#E67E22";

    private readonly IEinsatzService _einsatz;
    private readonly ITeamMobileTokenService? _teamNachrichten;
    private readonly IWarningService? _hinweise;
    private readonly IAuditLogService? _audit;
    // Entdopplung: Team|id → Kennung des angelegten Punkts, 24 h
    private readonly Dictionary<string, (string PunktId, DateTime Zeit)> _gesetzt = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sperre = new(1, 1);

    public Navigationspunkte(IEinsatzService einsatz, ITeamMobileTokenService? teamNachrichten = null,
        IWarningService? hinweise = null, IAuditLogService? audit = null)
    {
        _einsatz = einsatz;
        _teamNachrichten = teamNachrichten;
        _hinweise = hinweise;
        _audit = audit;
    }

    // ── EL → Team ───────────────────────────────────────────────────────

    /// <summary>Kartenpunkt als Ziel an das Team schicken; liefert die Mitteilung</summary>
    public async Task<TeamMitteilung> ZielSendenAsync(Team team, MapMarker punkt, string von)
    {
        var name = NavigationsLink.Name(string.IsNullOrWhiteSpace(punkt.Label) ? "Ziel" : punkt.Label);
        var ziel = new NavigationsZiel
        {
            PunktId = punkt.Id,
            Name = name,
            Latitude = Math.Round(punkt.Latitude, 6),
            Longitude = Math.Round(punkt.Longitude, 6)
        };
        var text = $"Ziel: {name}";
        if (_teamNachrichten is not null) _teamNachrichten.BroadcastTeamMessage(team.TeamId, text, ziel);
        else _einsatz.TeamMitteilungSpeichern(team.TeamId, text, ziel);

        await _einsatz.AddGlobalNoteWithSourceAsync($"[{team.TeamName}] Navigationsziel gesendet: {name} ({Koordinate(ziel.Latitude, ziel.Longitude)})",
            team.TeamId, team.TeamName, "Einsatzleitung", GlobalNotesEntryType.Manual, von);
        _audit?.Log("Navigationsziel an Team", $"{von}: {name}", team.TeamName);
        return _einsatz.TeamMitteilungen(team.TeamId).LastOrDefault(m => m.Ziel?.PunktId == punkt.Id) ?? new TeamMitteilung { TeamId = team.TeamId, Text = text, Ziel = ziel };
    }

    /// <summary>Mitteilung, wie sie in /lage und auf /ereignisse steht; mit Ziel samt Links fuer die Navi-Apps</summary>
    public static object MitteilungJson(TeamMitteilung m) => new
    {
        id = m.Id,
        text = m.Text,
        zeit = DateTime.SpecifyKind(m.ZeitUtc, DateTimeKind.Utc),
        ziel = m.Ziel is not { } z ? null : new
        {
            punktId = z.PunktId,
            name = z.Name,
            lat = z.Latitude,
            lng = z.Longitude,
            links = new
            {
                geo = NavigationsLink.Geo(z.Latitude, z.Longitude, z.Name),
                appleKarten = NavigationsLink.AppleKarten(z.Latitude, z.Longitude, z.Name),
                googleMaps = NavigationsLink.GoogleMaps(z.Latitude, z.Longitude),
                waze = NavigationsLink.Waze(z.Latitude, z.Longitude)
            }
        }
    };

    // ── Team → EL ───────────────────────────────────────────────────────

    /// <summary>Punkt am Standort des Teams setzen (POST /punkt)</summary>
    public async Task<TeamPunktErgebnis> PunktSetzenAsync(Team team, string id, string? text, double? lat, double? lng,
        double? genauigkeit, DateTime? zeitUtc, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return new(null, false, "id fehlt oder ist zu lang (höchstens 64 Zeichen).");
        var inhalt = text?.Trim() ?? "";
        if (inhalt.Length == 0 || inhalt.Length > MaxTextLaenge) return new(null, false, $"text fehlt oder ist zu lang (höchstens {MaxTextLaenge} Zeichen).");
        if (lat is not { } breite || lng is not { } laenge || !double.IsFinite(breite) || !double.IsFinite(laenge)
            || breite is < -90 or > 90 || laenge is < -180 or > 180)
            return new(null, false, "lat/lng fehlen oder liegen außerhalb des gültigen Bereichs.");
        if (genauigkeit is { } g && (!double.IsFinite(g) || g < 0)) return new(null, false, "genauigkeit ist ungültig.");
        if (zeitUtc is { } z && (z.Kind != DateTimeKind.Utc || z > nowUtc.AddMinutes(5))) return new(null, false, "zeit muss in UTC und nicht in der Zukunft liegen.");

        var schluessel = team.TeamId + "|" + id;
        // Nacheinander: Wiederholungen aus dem Funkloch duerfen nicht gleichzeitig zwei Punkte anlegen
        await _sperre.WaitAsync();
        try
        {
            foreach (var alt in _gesetzt.Where(p => p.Value.Zeit < nowUtc.AddHours(-24)).Select(p => p.Key).ToList())
                _gesetzt.Remove(alt);
            if (_gesetzt.TryGetValue(schluessel, out var schon)) return new(schon.PunktId, true);

            if (_einsatz.CurrentEinsatz.MapMarkers.Count(m => m.TeamId == team.TeamId) >= MaxPunkteJeTeam)
                return new(null, false, $"Höchstens {MaxPunkteJeTeam} Punkte je Team. Bitte per Funk melden.", GrenzeErreicht: true);

            var geortet = Ortszeit.AusUtc(zeitUtc ?? nowUtc);
            var beschreibung = $"Gesetzt von {team.TeamName} (Team-App) um {geortet:HH:mm:ss}"
                + (genauigkeit is { } m ? $", Genauigkeit ±{Math.Round(m):0} m" : "");
            var punkt = await _einsatz.AddMapMarkerAsync(new MapMarker
            {
                Label = $"{team.TeamName}: {inhalt}",
                Description = beschreibung,
                Latitude = Math.Round(breite, 6),
                Longitude = Math.Round(laenge, 6),
                Color = FarbeTeamPunkt,
                CreatedAt = geortet,
                TeamId = team.TeamId
            });
            _gesetzt[schluessel] = (punkt.Id, nowUtc);

            await _einsatz.AddGlobalNoteWithSourceAsync(
                $"[{team.TeamName}] Punkt gesetzt: {inhalt} ({Koordinate(punkt.Latitude, punkt.Longitude)}"
                    + (genauigkeit is { } n ? $", ±{Math.Round(n):0} m" : "") + ")",
                team.TeamId, team.TeamName, TeamAppSuche.QuelleTeamApp, GlobalNotesEntryType.Manual, TeamAppSuche.QuelleTeamApp);
            _hinweise?.AddWarning(new WarningEntry
            {
                Title = "Team-App",
                Message = $"{team.TeamName} hat einen Punkt gesetzt: {inhalt}",
                Level = WarningLevel.Info,
                TeamId = team.TeamId,
                Source = WarningRuleDefinition.Sources.TeamAppPunkt,
                NavigationUrl = "/einsatz-karte",
                Timestamp = Ortszeit.Jetzt
            });
            _audit?.Log("Punkt gesetzt (Team-App)", inhalt, team.TeamName);
            return new(punkt.Id, false);
        }
        finally
        {
            _sperre.Release();
        }
    }

    public static string Koordinate(double lat, double lng) =>
        string.Create(CultureInfo.InvariantCulture, $"{lat:F6}, {lng:F6}");
}

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Mitteilungen fuer die Einsatzleitung im Hintergrund ohne Push (#79, docs/APP_API.md native EL Abschnitt 11).
/// Sammelt Warnungen, Funk und Statusmeldungen der Teams waehrend eines laufenden Einsatzes im Arbeitsspeicher.
/// Die App sieht an <see cref="Stand"/> (in /status und /positions), ob es Neues gibt, und holt es mit <see cref="Seit"/>.
/// Texte nur mit Teamnamen: keine Vermissten, keine Ortsangaben, kein Funk- oder Fundinhalt (Sperrbildschirm).
/// </summary>
public sealed class ElMitteilungen : IHostedService, IDisposable
{
    public const int MaxGespeichert = 200;
    public const int MaxJeAntwort = 50;

    public sealed record Mitteilung(string Id, string Art, int Stufe, string? TeamId, string Text, DateTime Zeit);

    private readonly IEinsatzService _einsatz;
    private readonly IWarningService? _warnungen;
    private readonly object _sperre = new();
    private readonly List<(long Nr, Mitteilung M)> _liste = [];
    // Kennung dieses Serverlaufs: nach einem Neustart passt ein alter Stand der App nicht mehr
    private readonly string _lauf = Guid.NewGuid().ToString("N")[..6];
    private long _nr;
    private bool _aktiv;

    public ElMitteilungen(IEinsatzService einsatz, IWarningService? warnungen = null)
    {
        _einsatz = einsatz;
        _warnungen = warnungen;
        _aktiv = ElPositionService.Laeuft(_einsatz.CurrentEinsatz);
        _einsatz.TeamWarningTriggered += OnZeitwarnung;
        _einsatz.NoteAdded += OnNotiz;
        _einsatz.EinsatzChanged += OnEinsatz;
        if (_warnungen is not null) _warnungen.WarningAdded += OnWarnung;
    }

    public string Stand { get { lock (_sperre) return Kennung(_nr); } }

    /// <summary>Mitteilungen nach <paramref name="seit"/>, neueste zuletzt. Unbekannter Stand: leere Liste (keine Flut alter Mitteilungen)</summary>
    public (string Stand, IReadOnlyList<Mitteilung> Liste) Seit(string? seit)
    {
        lock (_sperre)
        {
            var stand = Kennung(_nr);
            if (!Lesen(seit, out var nr) || nr > _nr) return (stand, []);
            var neu = _liste.Where(e => e.Nr > nr).Select(e => e.M).ToList();
            if (neu.Count > MaxJeAntwort) neu = neu[^MaxJeAntwort..];
            return (stand, neu);
        }
    }

    private string Kennung(long nr) => $"{_lauf}-{nr}";

    private bool Lesen(string? seit, out long nr)
    {
        nr = 0;
        if (string.IsNullOrEmpty(seit)) return false;
        var teile = seit.Split('-');
        return teile.Length == 2 && teile[0] == _lauf && long.TryParse(teile[1], out nr) && nr >= 0;
    }

    /// <summary>Neue Mitteilung, nur waehrend ein Einsatz laeuft (ausser „einsatzende“)</summary>
    public void Hinzufuegen(string art, int stufe, string? teamId, string text, bool auchOhneEinsatz = false)
    {
        lock (_sperre)
        {
            if (!auchOhneEinsatz && !ElPositionService.Laeuft(_einsatz.CurrentEinsatz)) return;
            _nr++;
            _liste.Add((_nr, new Mitteilung(Kennung(_nr), art, stufe, teamId, text, DateTime.UtcNow)));
            if (_liste.Count > MaxGespeichert) _liste.RemoveRange(0, _liste.Count - MaxGespeichert);
        }
    }

    private string TeamName(string? teamId, string? ersatz = null) =>
        _einsatz.Teams.FirstOrDefault(t => t.TeamId == teamId)?.TeamName is { Length: > 0 } name ? name
        : string.IsNullOrWhiteSpace(ersatz) ? "Team" : ersatz;

    private void OnZeitwarnung(Team team, bool zweite)
    {
        var minuten = zweite ? team.SecondWarningMinutes : team.FirstWarningMinutes;
        Hinzufuegen("zeitwarnung", zweite ? 2 : 1, team.TeamId, $"{team.TeamName}: {minuten} min Suchzeit erreicht");
    }

    private void OnWarnung(WarningEntry w)
    {
        var team = TeamName(w.TeamId);
        switch (w.Source)
        {
            case WarningRuleDefinition.Sources.PhoneNoSignal:
                Hinzufuegen("funkstille", 2, w.TeamId, $"{team}: Handy sendet nicht"); break;
            case WarningRuleDefinition.Sources.CollarNoSignal:
                Hinzufuegen("funkstille", 2, w.TeamId, $"{team}: Halsband sendet nicht"); break;
            case WarningRuleDefinition.Sources.CollarOutOfBounds:
                Hinzufuegen("gebiet", 2, w.TeamId, $"{team}: Hund hat das Suchgebiet verlassen"); break;
            case WarningRuleDefinition.Sources.PhoneLowBattery:
                Hinzufuegen("akku", 1, w.TeamId, $"{team}: Handy-Akku niedrig"); break;
            case WarningRuleDefinition.Sources.CollarLowBattery:
                Hinzufuegen("akku", 1, w.TeamId, $"{team}: Halsband-Akku niedrig"); break;
            // Fund kommt als Warnung, ein normaler Punkt nur als Info (Navigationspunkte)
            case WarningRuleDefinition.Sources.TeamAppPunkt when w.Level >= WarningLevel.Warning:
                Hinzufuegen("fund", 2, w.TeamId, $"{team} meldet einen Fund"); break;
        }
    }

    private void OnNotiz(GlobalNotesEntry n)
    {
        if (string.Equals(n.SourceType, "Funk", StringComparison.OrdinalIgnoreCase))
        {
            var istTeam = _einsatz.Teams.Any(t => t.TeamId == n.SourceTeamId);
            Hinzufuegen("funk", 1, istTeam ? n.SourceTeamId : null,
                istTeam ? $"Neuer Funkspruch von {TeamName(n.SourceTeamId)}" : "Neuer Funkspruch");
        }
        else if (n.SourceType is "Mobile" || n.SourceType == TeamAppSuche.QuelleTeamApp)
        {
            var team = TeamName(n.SourceTeamId, n.SourceTeamName);
            // Nur die festen Texte der Team-App (POST /statusmeldung, POST /suche, beide CreatedBy „Team-App“) zeigen.
            // Freitext (Team-Webseite, Hub) kann Vermisste oder Orte nennen und gehoert nicht auf den Sperrbildschirm.
            var text = "neue Statusmeldung";
            if (n.CreatedBy == TeamAppSuche.QuelleTeamApp && n.Text is { } roh)
            {
                var praefix = $"[{n.SourceTeamName}] ";
                text = roh.StartsWith(praefix, StringComparison.Ordinal) ? roh[praefix.Length..] : roh;
            }
            Hinzufuegen("status", 1, n.SourceTeamId, $"{team}: {text}".Trim());
        }
    }

    private void OnEinsatz()
    {
        var aktiv = ElPositionService.Laeuft(_einsatz.CurrentEinsatz);
        lock (_sperre)
        {
            if (aktiv == _aktiv) return;
            _aktiv = aktiv;
            if (aktiv) _liste.Clear(); // neuer Einsatz: alte Mitteilungen weg, Zaehler laeuft weiter
        }
        if (!aktiv) Hinzufuegen("einsatzende", 1, null, "Einsatz beendet", auchOhneEinsatz: true);
    }

    // Als Hintergrunddienst registriert, damit die Ereignisse ab dem Serverstart gesammelt werden
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        _einsatz.TeamWarningTriggered -= OnZeitwarnung;
        _einsatz.NoteAdded -= OnNotiz;
        _einsatz.EinsatzChanged -= OnEinsatz;
        if (_warnungen is not null) _warnungen.WarningAdded -= OnWarnung;
    }
}

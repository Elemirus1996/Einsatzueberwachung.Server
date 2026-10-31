using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Live-Ereignisse fuer die native Team-Ansicht der App als Server-Sent Events (#119, docs/APP_API.md Abschnitt 9):
/// halsband, mitteilung, lage, stop. „lage“ nur, wenn sich fuer die App wirklich etwas aendert (TeamUpdated kommt sehr oft).
/// </summary>
public sealed class TeamAppEreignisse : IDisposable
{
    private sealed record Abo(string TeamId, Channel<string> Kanal, long Nummer);

    /// <summary>Hoechstens so viele gleichzeitige Live-Verbindungen je Team; bei mehr wird die aelteste beendet</summary>
    public const int MaxVerbindungenJeTeam = 10;
    private long _naechsteNummer;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IEinsatzService _einsatz;
    private readonly ICollarTrackingService _halsbaender;
    private readonly ITeamMobileTokenService _tokens;
    private readonly ConcurrentDictionary<Guid, Abo> _abos = new();
    private readonly ConcurrentDictionary<string, string> _signaturen = new(StringComparer.Ordinal);
    private readonly Timer _pruefTakt;

    /// <summary>
    /// Sicherheitsnetz: Nicht jede Aenderung meldet ein Ereignis (z. B. Halsband-Zuweisung setzt nur team.CollarId).
    /// Deshalb werden die Lage-Signaturen der verbundenen Teams zusaetzlich regelmaessig verglichen.
    /// </summary>
    public static readonly TimeSpan PruefIntervall = TimeSpan.FromSeconds(5);

    public TeamAppEreignisse(IEinsatzService einsatz, ICollarTrackingService halsbaender, ITeamMobileTokenService tokens)
    {
        _einsatz = einsatz;
        _halsbaender = halsbaender;
        _tokens = tokens;
        _halsbaender.CollarLocationReceived += OnHalsband;
        _einsatz.TeamMitteilungGespeichert += OnMitteilung;
        _einsatz.TeamUpdated += OnTeamGeaendert;
        _einsatz.TeamRemoved += OnTeamEntfernt;
        _einsatz.EinsatzChanged += OnEinsatzGeaendert;
        _tokens.GenerationChanged += OnGenerationGeaendert;
        _pruefTakt = new Timer(_ => AlleVerbundenenPruefen(), null, PruefIntervall, PruefIntervall);
    }

    /// <summary>Signaturen aller verbundenen Teams vergleichen und bei Aenderung „lage“ senden</summary>
    public void AlleVerbundenenPruefen()
    {
        try
        {
            foreach (var teamId in _abos.Values.Select(a => a.TeamId).Distinct().ToList())
                LageWennGeaendert(teamId);
        }
        catch { /* naechster Takt */ }
    }

    public int AnzahlVerbindungen => _abos.Count;

    public (Guid Id, ChannelReader<string> Leser) Abonnieren(string teamId)
    {
        var kanal = Channel.CreateBounded<string>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _abos[id] = new Abo(teamId, kanal, Interlocked.Increment(ref _naechsteNummer));

        var ueberzaehlig = _abos.Where(p => p.Value.TeamId == teamId)
            .OrderByDescending(p => p.Value.Nummer)
            .Skip(MaxVerbindungenJeTeam)
            .Select(p => p.Key)
            .ToList();
        // Eigenes Ereignis statt „stop“: Das verdraengte Geraet ist weiter angemeldet und darf nur nicht sofort neu verbinden
        foreach (var alt in ueberzaehlig) Beenden(alt, "verdraengt", new { wiederholenNachSec = VerdraengtWartenSec });
        _signaturen.TryAdd(teamId, Signatur(teamId));
        return (id, kanal.Reader);
    }

    public void Abmelden(Guid id)
    {
        if (_abos.TryRemove(id, out var abo)) abo.Kanal.Writer.TryComplete();
    }

    /// <summary>Ein SSE-Ereignis im Wire-Format</summary>
    public static string Format(string ereignis, object daten) =>
        $"event: {ereignis}\ndata: {JsonSerializer.Serialize(daten, Json)}\n\n";

    private void Senden(string teamId, string ereignis, object daten)
    {
        var text = Format(ereignis, daten);
        foreach (var abo in _abos.Values.Where(a => a.TeamId == teamId))
            abo.Kanal.Writer.TryWrite(text);
    }

    private void OnHalsband(string collarId, CollarLocation ort)
    {
        var team = _einsatz.Teams.FirstOrDefault(t => t.CollarId == collarId);
        if (team is null || _abos.Values.All(a => a.TeamId != team.TeamId)) return;
        Senden(team.TeamId, "halsband", new
        {
            lat = ort.Latitude,
            lng = ort.Longitude,
            zeit = AppZeit.Utc(ort.Timestamp),
            akku = ort.BatteryLevel
        });
    }

    private void OnMitteilung(TeamMitteilung m) =>
        Senden(m.TeamId, "mitteilung", Navigationspunkte.MitteilungJson(m));

    private void OnTeamGeaendert(Team team) => LageWennGeaendert(team.TeamId);

    private void OnEinsatzGeaendert()
    {
        // Suchgebiete aendern sich ueber EinsatzChanged
        foreach (var teamId in _abos.Values.Select(a => a.TeamId).Distinct().ToList())
            LageWennGeaendert(teamId);
    }

    private void OnTeamEntfernt(Team team) => StopFuer(a => a.TeamId == team.TeamId);

    // Einsatzende oder neuer Einsatz: alle App-Tokens sind ungueltig
    private void OnGenerationGeaendert() => StopFuer(_ => true);

    /// <summary>So lange wartet eine verdraengte App, bevor sie neu verbindet (sonst werfen sich Geraete im Kreis hinaus)</summary>
    public const int VerdraengtWartenSec = 60;

    private void Beenden(Guid id, string ereignis = "stop", object? daten = null)
    {
        if (_abos.TryGetValue(id, out var abo)) abo.Kanal.Writer.TryWrite(Format(ereignis, daten ?? new { }));
        Abmelden(id);
    }

    private void StopFuer(Func<Abo, bool> auswahl)
    {
        foreach (var id in _abos.Where(p => auswahl(p.Value)).Select(p => p.Key).ToList())
            Beenden(id);
    }

    private void LageWennGeaendert(string teamId)
    {
        if (_abos.Values.All(a => a.TeamId != teamId)) return;
        var neu = Signatur(teamId);
        var alt = _signaturen.GetValueOrDefault(teamId);
        if (neu == alt) return;
        _signaturen[teamId] = neu;
        Senden(teamId, "lage", new { });
    }

    /// <summary>Alles, was die Anzeige in /lage aendert – ohne laufende Suchzeit</summary>
    private string Signatur(string teamId)
    {
        var team = _einsatz.Teams.FirstOrDefault(t => t.TeamId == teamId);
        if (team is null) return "-";
        var gebiet = _einsatz.CurrentEinsatz.SearchAreas?.FirstOrDefault(a => a.Id == team.SearchAreaId);
        var gebietTeil = gebiet is null ? "" :
            $"{gebiet.Name}|{gebiet.Color}|{string.Join(";", (gebiet.Coordinates ?? []).Select(c => $"{c.Latitude:F6},{c.Longitude:F6}"))}";
        return string.Join("|", AppLage.SucheStatus(team), team.TeamName, team.DogName, team.HundefuehrerName,
            string.Join(",", team.HelferNames), team.CollarId, gebietTeil, _einsatz.CurrentEinsatz.IstEinsatz);
    }

    public void Dispose()
    {
        _pruefTakt.Dispose();
        _halsbaender.CollarLocationReceived -= OnHalsband;
        _einsatz.TeamMitteilungGespeichert -= OnMitteilung;
        _einsatz.TeamUpdated -= OnTeamGeaendert;
        _einsatz.TeamRemoved -= OnTeamEntfernt;
        _einsatz.EinsatzChanged -= OnEinsatzGeaendert;
        _tokens.GenerationChanged -= OnGenerationGeaendert;
        foreach (var id in _abos.Keys.ToList()) Abmelden(id);
    }
}

/// <summary>Zeiten fuer die App-Schnittstelle: immer UTC (docs/APP_API.md)</summary>
public static class AppZeit
{
    /// <summary>Kind Utc bleibt; unbestimmte Werte sind in dieser Anwendung Ortszeit (ITimeService)</summary>
    public static DateTime Utc(DateTime wert) => wert.Kind == DateTimeKind.Utc ? wert : Ortszeit.NachUtc(wert);

    public static long UnixMs(DateTime wert) => new DateTimeOffset(Utc(wert)).ToUnixTimeMilliseconds();
}

/// <summary>Aufbau der Antworten /lage und /tracks (testbar ohne Controller)</summary>
public static class AppLage
{
    public static string SucheStatus(Team team) =>
        team.IsRunning ? "laeuft"
        : team.IsManuallyPaused ? "kurzpause"
        : team.IsSucheBeendet ? "beendet"
        : "bereit";

    /// <summary>Block „suche“ von /lage und der Antwort auf POST /suche</summary>
    public static object Suche(Team team, bool darfSteuern)
    {
        var status = SucheStatus(team);
        return new
        {
            status,
            seit = status == "bereit" ? (DateTime?)null : AppZeit.Utc(team.StartTime),
            dauerSec = (long)team.ElapsedTime.TotalSeconds,
            darfSteuern
        };
    }

    public static object Erstellen(EinsatzData einsatz, Team team, string staffel, ICollarTrackingService halsbaender,
        IReadOnlyList<TeamMitteilung> mitteilungen, string modus, bool darfSteuern = true, object? datenschutz = null)
    {
        var gebiet = einsatz.SearchAreas?.FirstOrDefault(a => a.Id == team.SearchAreaId);
        var ort = string.IsNullOrWhiteSpace(team.CollarId) ? null : halsbaender.GetLatestLocation(team.CollarId);

        return new
        {
            mode = modus,
            serverZeit = DateTime.UtcNow,
            einsatz = new
            {
                text = string.Join(" · ", new[] { einsatz.Stichwort, einsatz.Einsatzort }.Where(s => !string.IsNullOrWhiteSpace(s))),
                uebung = !einsatz.IstEinsatz,
                staffel
            },
            team = new
            {
                teamId = team.TeamId,
                teamName = team.TeamName,
                dogName = team.DogName ?? "",
                hundefuehrerName = team.HundefuehrerName ?? "",
                helfer = team.HelferNames.Where(h => !string.IsNullOrWhiteSpace(h)).ToList()
            },
            suche = Suche(team, darfSteuern),
            datenschutz,
            suchgebiet = gebiet is null ? null : new
            {
                id = gebiet.Id,
                name = gebiet.Name,
                farbe = gebiet.Color,
                punkte = (gebiet.Coordinates ?? []).Select(c => new { lat = c.Latitude, lng = c.Longitude }).ToList()
            },
            halsband = ort is null ? null : new
            {
                id = team.CollarId,
                name = team.CollarName ?? team.CollarId,
                lat = ort.Latitude,
                lng = ort.Longitude,
                zeit = AppZeit.Utc(ort.Timestamp),
                akku = ort.BatteryLevel
            },
            mitteilungen = mitteilungen.TakeLast(EinsatzService.MaxTeamMitteilungen)
                .Select(Navigationspunkte.MitteilungJson).ToList()
        };
    }

    public static object Tracks(EinsatzData einsatz, Team team, IEinsatzService einsatzService, ICollarTrackingService halsbaender, long? seit)
    {
        var laeuft = team.IsRunning || team.IsManuallyPaused;
        object Punkt(double lat, double lng, DateTime t) => new { lat, lng, t = AppZeit.UnixMs(t) };

        var hund = laeuft && !string.IsNullOrWhiteSpace(team.CollarId)
            ? halsbaender.GetLocationHistory(team.CollarId).Select(p => (p.Latitude, p.Longitude, T: AppZeit.UnixMs(p.Timestamp), p.Timestamp))
            : [];
        var mensch = laeuft
            ? einsatzService.GetPhoneTrackHistory(team.TeamId).Select(p => (p.Latitude, p.Longitude, T: AppZeit.UnixMs(p.Timestamp), p.Timestamp))
            : [];

        var nachSeit = (IEnumerable<(double Latitude, double Longitude, long T, DateTime Timestamp)> punkte) =>
            punkte.Where(p => seit is null || p.T > seit).OrderBy(p => p.T).Select(p => Punkt(p.Latitude, p.Longitude, p.Timestamp)).ToList();

        return new
        {
            serverZeit = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            laufend = new { hund = nachSeit(hund), mensch = nachSeit(mensch) },
            abgeschlossen = seit is not null ? null : (einsatz.CompletedSearches ?? [])
                .Where(s => s.TeamId == team.TeamId)
                .OrderBy(s => s.SearchStart)
                .Select(s => new
                {
                    sucheId = s.Id,
                    beginn = AppZeit.Utc(s.SearchStart),
                    ende = AppZeit.Utc(s.SearchEnd),
                    tracks = s.Tracks.Select(t => new
                    {
                        art = t.TrackType == Einsatzueberwachung.Domain.Models.Enums.TrackType.HumanTrack ? "mensch" : "hund",
                        farbe = t.Color,
                        punkte = t.Points.Select(p => Punkt(p.Latitude, p.Longitude, p.Timestamp)).ToList()
                    }).ToList()
                }).ToList()
        };
    }
}

/// <summary>Entdoppelt Statusmeldungen der App (gleiche id → nur eine Notiz), haelt 24 h</summary>
public sealed class AppStatusmeldungen
{
    private readonly ConcurrentDictionary<string, DateTime> _gesehen = new(StringComparer.Ordinal);

    public bool ErstesMal(string teamId, string id, DateTime nowUtc)
    {
        foreach (var alt in _gesehen.Where(p => p.Value < nowUtc.AddHours(-24)).Select(p => p.Key).ToList())
            _gesehen.TryRemove(alt, out _);
        return _gesehen.TryAdd(teamId + "|" + id, nowUtc);
    }
}

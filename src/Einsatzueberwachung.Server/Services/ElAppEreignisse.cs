using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Live-Ereignisse der nativen Einsatzleitung (GET /api/el/app/v1/ereignisse, docs/APP_API.md Abschnitt 8).
/// Anders als in der Team-Rolle bleibt die Verbindung bei Einsatzende offen („einsatz“ mit aktiv = false).
/// </summary>
public sealed class ElAppEreignisse : IDisposable
{
    private sealed record Abo(string Token, Channel<string> Kanal, long Nummer);

    /// <summary>Hoechstens so viele gleichzeitige Live-Verbindungen je Token; bei mehr wird die aelteste verdraengt</summary>
    public const int MaxVerbindungenJeToken = 10;
    private long _naechsteNummer;

    private readonly IEinsatzService _einsatz;
    private readonly ICollarTrackingService _halsbaender;
    private readonly ElPositionService _positionen;
    private readonly ConcurrentDictionary<Guid, Abo> _abos = new();
    private bool _aktiv;
    // Antworten auf Notizen/Funk melden nur EinsatzChanged – deshalb Anzahl je Notiz vergleichen
    private readonly Dictionary<string, int> _antworten = new(StringComparer.Ordinal);
    private readonly object _antwortenSperre = new();

    public ElAppEreignisse(IEinsatzService einsatz, ICollarTrackingService halsbaender, ElPositionService positionen)
    {
        _einsatz = einsatz;
        _halsbaender = halsbaender;
        _positionen = positionen;
        _aktiv = ElPositionService.Laeuft(einsatz.CurrentEinsatz);
        AntwortenZaehlen();

        _einsatz.TeamAdded += OnTeam;
        _einsatz.TeamUpdated += OnTeam;
        _einsatz.TeamRemoved += OnTeam;
        _einsatz.NoteAdded += OnNotiz;
        _einsatz.ElNotizAdded += OnElNotiz;
        _einsatz.TeamWarningTriggered += OnWarnung;
        _einsatz.VermisstenInfoChanged += OnVermisste;
        _einsatz.VermisstenAdded += OnVermissteProfil;
        _einsatz.VermisstenUpdated += OnVermissteProfil;
        _einsatz.VermisstenRemoved += OnVermissteEntfernt;
        _einsatz.EinsatzChanged += OnEinsatz;
        _einsatz.TeamPhoneLocationChanged += OnHandy;
        _einsatz.MapMarkerChanged += OnPunkt;
        _halsbaender.CollarLocationReceived += OnHalsband;
        _positionen.Geaendert += OnEl;
    }

    public int AnzahlVerbindungen => _abos.Count;

    /// <summary>Verbindungen werden je Token gezaehlt; gespeichert wird nur der Hash, nie das Token selbst</summary>
    public (Guid Id, ChannelReader<string> Leser) Abonnieren(string token)
    {
        var schluessel = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var kanal = Channel.CreateBounded<string>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _abos[id] = new Abo(schluessel, kanal, Interlocked.Increment(ref _naechsteNummer));

        var ueberzaehlig = _abos.Where(p => p.Value.Token == schluessel)
            .OrderByDescending(p => p.Value.Nummer)
            .Skip(MaxVerbindungenJeToken)
            .Select(p => p.Key)
            .ToList();
        foreach (var alt in ueberzaehlig)
        {
            if (_abos.TryGetValue(alt, out var abo))
                abo.Kanal.Writer.TryWrite(TeamAppEreignisse.Format("verdraengt", new { wiederholenNachSec = TeamAppEreignisse.VerdraengtWartenSec }));
            Abmelden(alt);
        }
        return (id, kanal.Reader);
    }

    public void Abmelden(Guid id)
    {
        if (_abos.TryRemove(id, out var abo)) abo.Kanal.Writer.TryComplete();
    }

    /// <summary>Ereignis an alle Verbindungen, z. B. nach einer Aktion ohne eigenes Domain-Ereignis (Notiz geloescht)</summary>
    public void Senden(string ereignis, object daten)
    {
        if (_abos.IsEmpty) return;
        var text = TeamAppEreignisse.Format(ereignis, daten);
        foreach (var abo in _abos.Values) abo.Kanal.Writer.TryWrite(text);
    }

    private void OnTeam(Team team) => Senden("team", new { teamId = team.TeamId });

    /// <summary>Funk steht als Notiz mit Quelle „Funk“ im Einsatz – die App sieht beides ueber /notizen</summary>
    public static bool IstFunk(GlobalNotesEntry notiz) => string.Equals(notiz.SourceType, "Funk", StringComparison.OrdinalIgnoreCase);

    private void OnNotiz(GlobalNotesEntry notiz)
    {
        lock (_antwortenSperre) _antworten[notiz.Id] = notiz.ReplyCount;
        Senden(IstFunk(notiz) ? "funk" : "notiz", new { id = notiz.Id });
    }

    private void OnElNotiz() => Senden("notiz", new { id = _einsatz.CurrentEinsatz.ElNotizen?.FirstOrDefault()?.Id });

    private void AntwortenZaehlen()
    {
        var geaendert = new List<GlobalNotesEntry>();
        lock (_antwortenSperre)
        {
            foreach (var notiz in _einsatz.GlobalNotes.ToList())
            {
                if (_antworten.TryGetValue(notiz.Id, out var anzahl) && anzahl != notiz.ReplyCount) geaendert.Add(notiz);
                _antworten[notiz.Id] = notiz.ReplyCount;
            }
        }
        foreach (var notiz in geaendert) Senden(IstFunk(notiz) ? "funk" : "notiz", new { id = notiz.Id });
    }

    private void OnVermisste() => Senden("vermisste", new { });
    private void OnVermissteProfil(VermisstenInfo _) => OnVermisste();
    private void OnVermissteEntfernt(Guid _) => OnVermisste();

    // Kartenpunkt (Variante C); text nur bei einem neuen Punkt eines Teams – dafuer hat die App eine eigene Tonart
    private void OnPunkt(MapMarker punkt, string aenderung) => Senden("punkt", new
    {
        id = punkt.Id,
        teamId = string.IsNullOrWhiteSpace(punkt.TeamId) ? null : punkt.TeamId,
        aenderung,
        text = aenderung == KartenpunktAenderung.Neu && !string.IsNullOrWhiteSpace(punkt.TeamId) ? punkt.Label : null
    });

    private void OnWarnung(Team team, bool zweite)
    {
        var stufe = zweite ? 2 : 1;
        var minuten = zweite ? team.SecondWarningMinutes : team.FirstWarningMinutes;
        Senden("warnung", new { teamId = team.TeamId, stufe, text = $"{team.TeamName}: {minuten} min Suchzeit erreicht" });
    }

    private void OnEinsatz()
    {
        AntwortenZaehlen();
        var aktiv = ElPositionService.Laeuft(_einsatz.CurrentEinsatz);
        if (aktiv == _aktiv) return;
        _aktiv = aktiv;
        Senden("einsatz", new { aktiv });
    }

    private void OnHalsband(string collarId, CollarLocation ort)
    {
        if (_abos.IsEmpty) return;
        var team = _einsatz.Teams.FirstOrDefault(t => t.CollarId == collarId);
        Senden("position", new
        {
            art = "halsband", id = collarId, teamId = team?.TeamId,
            lat = ort.Latitude, lng = ort.Longitude, zeit = AppZeit.Utc(ort.Timestamp), akku = ort.BatteryLevel
        });
    }

    private void OnHandy(string teamId, string _, TeamPhoneLocation ort) => Senden("position", new
    {
        art = "handy", id = teamId, teamId,
        lat = ort.Latitude, lng = ort.Longitude, zeit = AppZeit.Utc(ort.Timestamp), akku = ort.BatteryPercent
    });

    private void OnEl(ElPosition p) => Senden("position", new
    {
        art = "el", id = p.Geraet, teamId = (string?)null,
        lat = p.Latitude, lng = p.Longitude, zeit = AppZeit.Utc(p.Zeit), akku = p.BatteryPercent
    });

    public void Dispose()
    {
        _einsatz.TeamAdded -= OnTeam;
        _einsatz.TeamUpdated -= OnTeam;
        _einsatz.TeamRemoved -= OnTeam;
        _einsatz.NoteAdded -= OnNotiz;
        _einsatz.ElNotizAdded -= OnElNotiz;
        _einsatz.TeamWarningTriggered -= OnWarnung;
        _einsatz.VermisstenInfoChanged -= OnVermisste;
        _einsatz.VermisstenAdded -= OnVermissteProfil;
        _einsatz.VermisstenUpdated -= OnVermissteProfil;
        _einsatz.VermisstenRemoved -= OnVermissteEntfernt;
        _einsatz.EinsatzChanged -= OnEinsatz;
        _einsatz.TeamPhoneLocationChanged -= OnHandy;
        _einsatz.MapMarkerChanged -= OnPunkt;
        _halsbaender.CollarLocationReceived -= OnHalsband;
        _positionen.Geaendert -= OnEl;
        foreach (var id in _abos.Keys.ToList()) Abmelden(id);
    }
}

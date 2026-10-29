using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Meldet neue Funksprueche an die Live-Ereignisse der EL-App. Der RadioService ist scoped (je Browser-Sitzung),
/// deshalb laeuft die Meldung ueber diesen Singleton.
/// </summary>
public sealed class FunkEreignisse
{
    public event Action<string>? Neu;

    public void Melden(string funkId) => Neu?.Invoke(funkId);
}

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
    private readonly FunkEreignisse _funk;
    private readonly ConcurrentDictionary<Guid, Abo> _abos = new();
    private bool _aktiv;

    public ElAppEreignisse(IEinsatzService einsatz, ICollarTrackingService halsbaender, ElPositionService positionen, FunkEreignisse funk)
    {
        _einsatz = einsatz;
        _halsbaender = halsbaender;
        _positionen = positionen;
        _funk = funk;
        _aktiv = ElPositionService.Laeuft(einsatz.CurrentEinsatz);

        _einsatz.TeamAdded += OnTeam;
        _einsatz.TeamUpdated += OnTeam;
        _einsatz.TeamRemoved += OnTeam;
        _einsatz.NoteAdded += OnNotiz;
        _einsatz.TeamWarningTriggered += OnWarnung;
        _einsatz.VermisstenInfoChanged += OnVermisste;
        _einsatz.VermisstenAdded += OnVermissteProfil;
        _einsatz.VermisstenUpdated += OnVermissteProfil;
        _einsatz.VermisstenRemoved += OnVermissteEntfernt;
        _einsatz.EinsatzChanged += OnEinsatz;
        _einsatz.TeamPhoneLocationChanged += OnHandy;
        _halsbaender.CollarLocationReceived += OnHalsband;
        _positionen.Geaendert += OnEl;
        _funk.Neu += OnFunk;
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

    private void Senden(string ereignis, object daten)
    {
        if (_abos.IsEmpty) return;
        var text = TeamAppEreignisse.Format(ereignis, daten);
        foreach (var abo in _abos.Values) abo.Kanal.Writer.TryWrite(text);
    }

    private void OnTeam(Team team) => Senden("team", new { teamId = team.TeamId });

    private void OnNotiz(GlobalNotesEntry notiz) => Senden("notiz", new { id = notiz.Id });

    private void OnFunk(string funkId) => Senden("funk", new { id = funkId });

    private void OnVermisste() => Senden("vermisste", new { });
    private void OnVermissteProfil(VermisstenInfo _) => OnVermisste();
    private void OnVermissteEntfernt(Guid _) => OnVermisste();

    private void OnWarnung(Team team, bool zweite)
    {
        var stufe = zweite ? 2 : 1;
        var minuten = zweite ? team.SecondWarningMinutes : team.FirstWarningMinutes;
        Senden("warnung", new { teamId = team.TeamId, stufe, text = $"{team.TeamName}: {minuten} min Suchzeit erreicht" });
    }

    private void OnEinsatz()
    {
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
        _einsatz.TeamWarningTriggered -= OnWarnung;
        _einsatz.VermisstenInfoChanged -= OnVermisste;
        _einsatz.VermisstenAdded -= OnVermissteProfil;
        _einsatz.VermisstenUpdated -= OnVermissteProfil;
        _einsatz.VermisstenRemoved -= OnVermissteEntfernt;
        _einsatz.EinsatzChanged -= OnEinsatz;
        _einsatz.TeamPhoneLocationChanged -= OnHandy;
        _halsbaender.CollarLocationReceived -= OnHalsband;
        _positionen.Geaendert -= OnEl;
        _funk.Neu -= OnFunk;
        foreach (var id in _abos.Keys.ToList()) Abmelden(id);
    }
}

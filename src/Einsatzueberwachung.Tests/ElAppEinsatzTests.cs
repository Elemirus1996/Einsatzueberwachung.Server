using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Divera;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Native Einsatzleitung, Stufe 5: Einsatz starten und beenden (#125)</summary>
public sealed class ElAppEinsatzTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-eleinsatz-" + Guid.NewGuid().ToString("N"));
    private readonly LeitungAuthStore _leitung;
    private readonly EinsatzService _einsatz = new();
    private readonly ElAppTokenService _tokens;
    private readonly ElAppAktionen _aktionen = new();
    private readonly string _token;
    private readonly List<PdfBerichtOptionen> _pdfOptionen = [];
    private readonly EinsatzAbschlussTests.Recorder<IArchivService> _archiv;
    private readonly EinsatzAbschlussService _abschluss;
    private DateTime? _archiviertesEnde;
    private List<string>? _archiviertesPersonal;
    private List<string>? _archivierteHunde;
    private readonly PersonalEntry _anna = new() { Vorname = "Anna", Nachname = "Berg", IsActive = true, Skills = PersonalSkills.Einsatzleiter };
    private readonly PersonalEntry _ben = new() { Vorname = "Ben", Nachname = "Kurz", IsActive = true, Skills = PersonalSkills.Fuehrungsassistent };
    private readonly DogEntry _rex = new() { Name = "Rex", IsActive = true };
    private readonly DogEntry _bello = new() { Name = "Bello", IsActive = true };

    private sealed class Uhr : ITimeService
    {
        public DateTime Now => Ortszeit.Jetzt;
        public void Refresh() { }
    }

    public ElAppEinsatzTests()
    {
        Directory.CreateDirectory(_dir);
        _leitung = new LeitungAuthStore(_dir);
        Assert.Null(_leitung.Setup(_leitung.SetupCode!, "Rettungshund2026", "Rettungshund2026"));
        _tokens = new ElAppTokenService(RandomNumberGenerator.GetBytes(64), _leitung);
        _token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(1));
        _archiv = EinsatzAbschlussTests.Recorder<IArchivService>.Create((m, args) =>
        {
            if (m.Name != nameof(IArchivService.ArchiveEinsatzAsync)) return null;
            _archiviertesEnde = ((EinsatzData)args![0]!).EinsatzEnde;
            _archiviertesPersonal = (List<string>)args[3]!;
            _archivierteHunde = (List<string>)args[4]!;
            return Task.FromResult(ArchivedEinsatz.FromEinsatzData((EinsatzData)args[0]!, (string)args[1]!, (string)args[2]!, Ortszeit.Jetzt));
        });
        var pdf = EinsatzAbschlussTests.Recorder<IPdfExportService>.Create((_, args) =>
        {
            if (args?.Length == 2 && args[1] is PdfBerichtOptionen o) _pdfOptionen.Add(o);
            return Task.FromResult(new PdfExportResult { Success = true });
        });
        _abschluss = new EinsatzAbschlussService(_einsatz, _archiv.Proxy, pdf.Proxy, new Uhr());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ElAppController Controller(IArchivService? archiv = null, IPdfExportService? pdfExport = null)
    {
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) =>
            m.Name == nameof(ISettingsService.GetStaffelSettingsAsync) ? Task.FromResult(new StaffelSettings { StaffelName = "RHS Test" }) : null);
        var stammdaten = EinsatzAbschlussTests.Recorder<IMasterDataService>.Create((m, _) => m.Name switch
        {
            nameof(IMasterDataService.GetPersonalListAsync) => Task.FromResult(new List<PersonalEntry> { _ben, _anna }),
            nameof(IMasterDataService.GetDogListAsync) => Task.FromResult(new List<DogEntry> { _rex, _bello }),
            _ => null
        });
        var divera = EinsatzAbschlussTests.Recorder<IDiveraService>.Create((m, args) => m.Name switch
        {
            "get_IsConfigured" => true,
            nameof(IDiveraService.GetActiveAlarmsAsync) => Task.FromResult(new List<DiveraAlarm> { Alarm() }),
            nameof(IDiveraService.GetAlarmByIdAsync) => Task.FromResult((int)args![0]! == 7 ? Alarm() : null),
            _ => null
        });
        var c = new ElAppController(_tokens, new ElPositionService(_einsatz), new LeitungsGeraetNamen(new VpnDeviceStore(_dir)),
            new PublicAccess(5002), NullLogger<ElAppController>.Instance, null,
            _leitung, new LoginLockout(), null, _einsatz, null, settings.Proxy, null, null, _aktionen,
            abschluss: _abschluss, stammdaten: stammdaten.Proxy, divera: divera.Proxy, zeit: new Uhr(),
            archiv: archiv, pdfExport: pdfExport);
        var http = new DefaultHttpContext();
        http.Connection.LocalPort = 5000;
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.9");
        http.Request.Headers.Authorization = "Bearer " + _token;
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static DiveraAlarm Alarm() => new()
    {
        Id = 7, Title = "Vermisste Person", Address = "Waldweg 1, Testdorf", Lat = 49.31, Lng = 8.43, Caller = "ILS", Date = Ortszeit.Jetzt.AddMinutes(-20)
    };

    private static string NeueId() => Guid.NewGuid().ToString();

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    private static ElAppController.ElEinsatzStart Start(string? ort = "Testwald", string? szenario = "Flaeche", int? divera = null, bool uebung = false) =>
        new(NeueId(), "VP", ort, null, null, szenario, uebung, "Anna Berg", null, divera);

    [Fact]
    public async Task Vorlage_ListenUndErgebnisse()
    {
        var vorlage = Json(await Controller().GetEinsatzVorlage());
        Assert.False(vorlage.GetProperty("aktiv").GetBoolean());
        Assert.Equal("Anna Berg", Assert.Single(vorlage.GetProperty("einsatzleiter").EnumerateArray()).GetString());
        Assert.Equal("Ben Kurz", Assert.Single(vorlage.GetProperty("fuehrungsassistenten").EnumerateArray()).GetString());
        Assert.Equal(7, Assert.Single(vorlage.GetProperty("divera").EnumerateArray()).GetProperty("id").GetInt32());
        Assert.Equal(["Streets", "Topographic", "Satellite"], vorlage.GetProperty("kartenarten").EnumerateArray().Select(k => k.GetProperty("wert").GetString()).ToArray());

        var ergebnisse = Json(Controller().GetErgebnisse()).EnumerateArray().ToList();
        Assert.Contains(ergebnisse, e => e.GetProperty("wert").GetString() == "Uebung abgeschlossen" && e.GetProperty("anzeige").GetString() == "Übung abgeschlossen");
    }

    [Fact]
    public async Task Start_VonHandUndAusDivera_LaeuftSchon409_Pflichtangaben400()
    {
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzStarten(Start(szenario: "Unbestimmt")));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzStarten(Start(ort: " ")));
        Assert.IsType<NotFoundObjectResult>(await Controller().EinsatzStarten(Start(ort: null, divera: 99)));

        var ok = await Controller().EinsatzStarten(Start(ort: null, divera: 7, uebung: true));
        Assert.True(Json(ok).GetProperty("einsatz").GetProperty("aktiv").GetBoolean());
        var e = _einsatz.CurrentEinsatz;
        Assert.Equal("Waldweg 1, Testdorf", e.Einsatzort);
        Assert.Equal((49.31, 8.43), e.ElwPosition);
        Assert.Equal("Anna Berg", e.Einsatzleiter);
        Assert.Equal("RHS Test", e.StaffelName);
        Assert.False(e.IstEinsatz);
        Assert.Equal(EinsatzSzenarioType.Flaeche, e.Szenario);

        Assert.IsType<ConflictObjectResult>(await Controller().EinsatzStarten(Start()));
    }

    [Fact]
    public async Task Beenden_ArchiviertMitKartenartUndTeams_FalscheWerte400_OhneEinsatz409()
    {
        Assert.IsType<OkObjectResult>(await Controller().EinsatzStarten(Start()));
        var team = await _einsatz.AddTeamAsync(new Team { TeamName = "Team Rex", HundefuehrerName = "Anna Berg" });
        _einsatz.CurrentEinsatz.EinsatzDatum = Ortszeit.Jetzt.AddHours(-2); // Einsatz laeuft seit zwei Stunden

        ElAppController.ElEinsatzEnde Ende(string? ergebnis = "Suche erfolglos abgeschlossen", bool geprueft = true, string? karte = "Topographic") =>
            new(NeueId(), ergebnis, geprueft, "ohne Befund", null, new ElAppController.ElPdfWahl(true, karte, [team.TeamId]));

        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende(ergebnis: "Erfunden")));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende(geprueft: false)));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende(karte: "Google")));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende(karte: "2")));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende() with { Endzeit = "18:00" }));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende() with { Endzeit = "2026-10-31T17:45:00" })); // ohne Zone
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende() with { Endzeit = DateTime.UtcNow.AddHours(1).ToString("O") }));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende() with { Endzeit = DateTime.UtcNow.AddDays(-1).ToString("O") }));

        var endeUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddSeconds(-30), DateTimeKind.Utc);
        endeUtc = endeUtc.AddTicks(-(endeUtc.Ticks % TimeSpan.TicksPerSecond));
        var ok = Json(await Controller().EinsatzBeenden(Ende() with { Endzeit = endeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ") }));
        Assert.True(ok.GetProperty("pdf").GetProperty("erstellt").GetBoolean());
        Assert.Equal(Ortszeit.AusUtc(endeUtc), _archiviertesEnde); // fuer den Bericht in Ortszeit
        var optionen = Assert.Single(_pdfOptionen);
        Assert.Equal(MapTileType.Topographic, optionen.Kartenart);
        Assert.Equal([team.TeamId], optionen.TeamIds);
        Assert.False(ElPositionService.Laeuft(_einsatz.CurrentEinsatz));

        Assert.IsType<ConflictObjectResult>(await Controller().EinsatzBeenden(Ende()));
        Assert.Equal(1, _archiv.Calls.Count(c => c == nameof(IArchivService.ArchiveEinsatzAsync)));
    }

    [Fact]
    public async Task VorOrt_ListenMitVorauswahl_BeendenUebernimmtAuswahlOderVorauswahl()
    {
        Assert.IsType<OkObjectResult>(await Controller().EinsatzStarten(Start()));
        await _einsatz.AddTeamAsync(new Team { TeamName = "Team Rex", HundefuehrerName = "Anna Berg", DogName = "Rex" });

        var vorOrt = Json(await Controller().GetVorOrt());
        var personal = vorOrt.GetProperty("personal").EnumerateArray().ToList();
        Assert.Equal(["Anna Berg", "Ben Kurz"], personal.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(_anna.Id, personal[0].GetProperty("id").GetString());
        Assert.Equal([true, false], personal.Select(p => p.GetProperty("vorausgewaehlt").GetBoolean()));
        var hunde = vorOrt.GetProperty("hunde").EnumerateArray().ToList();
        Assert.Equal(["Bello", "Rex"], hunde.Select(d => d.GetProperty("name").GetString()));
        Assert.Equal([false, true], hunde.Select(d => d.GetProperty("vorausgewaehlt").GetBoolean()));

        var ohneToken = Controller();
        ohneToken.HttpContext.Request.Headers.Authorization = "Bearer falsch";
        Assert.Equal(410, ((IStatusCodeActionResult)await ohneToken.GetVorOrt()).StatusCode);
        var oeffentlich = Controller();
        oeffentlich.HttpContext.Connection.LocalPort = 5002;
        Assert.IsType<NotFoundResult>(await oeffentlich.GetVorOrt());

        // Auswahl der App geht vor, unbekannte IDs fallen weg, leere Liste = niemand
        Assert.IsType<OkObjectResult>(await Controller().EinsatzBeenden(new ElAppController.ElEinsatzEnde(
            NeueId(), "Suche erfolglos abgeschlossen", true, PersonalIds: [_ben.Id, "unbekannt"], HundeIds: [])));
        Assert.Equal(["Ben Kurz"], _archiviertesPersonal);
        Assert.Empty(_archivierteHunde!);
    }

    [Fact]
    public async Task Beenden_OhneAuswahl_UebernimmtVorauswahlAusDenTeams()
    {
        Assert.IsType<OkObjectResult>(await Controller().EinsatzStarten(Start()));
        await _einsatz.AddTeamAsync(new Team { TeamName = "Team Rex", HundefuehrerName = "Anna Berg", DogName = "Rex" });

        Assert.IsType<OkObjectResult>(await Controller().EinsatzBeenden(new ElAppController.ElEinsatzEnde(NeueId(), "Suche erfolglos abgeschlossen", true)));
        Assert.Equal(["Anna Berg"], _archiviertesPersonal);
        Assert.Equal(["Rex"], _archivierteHunde);
    }

    [Fact]
    public async Task Bericht_PdfZurArchivId_MitOptionen_Unbekannt404_OhneToken410_Oeffentlich404()
    {
        var einsatzDaten = new EinsatzData { EinsatzNummer = "2026-042", EinsatzDatum = Ortszeit.Jetzt.AddHours(-3) };
        var archiviert = ArchivedEinsatz.FromEinsatzData(einsatzDaten, "Suche erfolglos abgeschlossen", "", Ortszeit.Jetzt);
        var archiv = EinsatzAbschlussTests.Recorder<IArchivService>.Create((m, args) =>
            m.Name == nameof(IArchivService.GetByIdAsync) ? Task.FromResult((string)args![0]! == archiviert.Id ? archiviert : null) : null);
        var optionen = new List<PdfBerichtOptionen>();
        var pdf = EinsatzAbschlussTests.Recorder<IPdfExportService>.Create((_, args) =>
        {
            if (args?.Length == 2 && args[1] is PdfBerichtOptionen o) optionen.Add(o);
            return Task.FromResult(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        });

        var datei = Assert.IsType<FileContentResult>(await Controller(archiv.Proxy, pdf.Proxy).GetBericht(archiviert.Id, "Satellite", "t1,t2", "0"));
        Assert.Equal("application/pdf", datei.ContentType);
        Assert.Equal("einsatzbericht-2026-042.pdf", datei.FileDownloadName);
        var o = Assert.Single(optionen);
        Assert.Equal(MapTileType.Satellite, o.Kartenart);
        Assert.False(o.Tracks);
        Assert.Equal(["t1", "t2"], o.TeamIds!.OrderBy(t => t));

        Assert.IsType<NotFoundObjectResult>(await Controller(archiv.Proxy, pdf.Proxy).GetBericht("unbekannt"));

        var ohneToken = Controller(archiv.Proxy, pdf.Proxy);
        ohneToken.HttpContext.Request.Headers.Authorization = "Bearer falsch";
        Assert.Equal(410, ((IStatusCodeActionResult)await ohneToken.GetBericht(archiviert.Id)).StatusCode);
        var oeffentlich = Controller(archiv.Proxy, pdf.Proxy);
        oeffentlich.HttpContext.Connection.LocalPort = 5002;
        Assert.IsType<NotFoundResult>(await oeffentlich.GetBericht(archiviert.Id));
    }
}

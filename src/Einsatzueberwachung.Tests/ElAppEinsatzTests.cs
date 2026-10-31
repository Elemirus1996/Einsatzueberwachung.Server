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
            m.Name == nameof(IArchivService.ArchiveEinsatzAsync)
                ? Task.FromResult(ArchivedEinsatz.FromEinsatzData((EinsatzData)args![0]!, (string)args[1]!, (string)args[2]!, Ortszeit.Jetzt))
                : null);
        var pdf = EinsatzAbschlussTests.Recorder<IPdfExportService>.Create((_, args) =>
        {
            if (args?.Length == 2 && args[1] is PdfBerichtOptionen o) _pdfOptionen.Add(o);
            return Task.FromResult(new PdfExportResult { Success = true });
        });
        _abschluss = new EinsatzAbschlussService(_einsatz, _archiv.Proxy, pdf.Proxy, new Uhr());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ElAppController Controller()
    {
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) =>
            m.Name == nameof(ISettingsService.GetStaffelSettingsAsync) ? Task.FromResult(new StaffelSettings { StaffelName = "RHS Test" }) : null);
        var stammdaten = EinsatzAbschlussTests.Recorder<IMasterDataService>.Create((m, _) => m.Name switch
        {
            nameof(IMasterDataService.GetPersonalListAsync) => Task.FromResult(new List<PersonalEntry>
            {
                new() { Vorname = "Anna", Nachname = "Berg", IsActive = true, Skills = PersonalSkills.Einsatzleiter },
                new() { Vorname = "Ben", Nachname = "Kurz", IsActive = true, Skills = PersonalSkills.Fuehrungsassistent }
            }),
            nameof(IMasterDataService.GetDogListAsync) => Task.FromResult(new List<DogEntry>()),
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
            abschluss: _abschluss, stammdaten: stammdaten.Proxy, divera: divera.Proxy, zeit: new Uhr());
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

        ElAppController.ElEinsatzEnde Ende(string? ergebnis = "Suche erfolglos abgeschlossen", bool geprueft = true, string? karte = "Topographic") =>
            new(NeueId(), ergebnis, geprueft, "ohne Befund", null, new ElAppController.ElPdfWahl(true, karte, [team.TeamId]));

        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende(ergebnis: "Erfunden")));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende(geprueft: false)));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende(karte: "Google")));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende(karte: "2")));
        Assert.IsType<BadRequestObjectResult>(await Controller().EinsatzBeenden(Ende() with { Endzeit = "18 Uhr" }));

        var ok = Json(await Controller().EinsatzBeenden(Ende()));
        Assert.True(ok.GetProperty("pdf").GetProperty("erstellt").GetBoolean());
        var optionen = Assert.Single(_pdfOptionen);
        Assert.Equal(MapTileType.Topographic, optionen.Kartenart);
        Assert.Equal([team.TeamId], optionen.TeamIds);
        Assert.False(ElPositionService.Laeuft(_einsatz.CurrentEinsatz));

        Assert.IsType<ConflictObjectResult>(await Controller().EinsatzBeenden(Ende()));
        Assert.Equal(1, _archiv.Calls.Count(c => c == nameof(IArchivService.ArchiveEinsatzAsync)));
    }
}

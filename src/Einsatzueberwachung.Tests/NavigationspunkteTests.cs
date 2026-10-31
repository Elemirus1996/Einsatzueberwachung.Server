using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Navigationsziele EL → Team und Punkte Team → EL (Variante C, docs/APP_API.md Team-Rolle Abschnitt 13)</summary>
public sealed class NavigationspunkteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-navi-" + Guid.NewGuid().ToString("N"));
    private readonly EinsatzService _einsatz = new();
    private readonly TeamMobileTokenService _teamTokens;
    private readonly CollarTrackingService _halsbaender;
    private readonly EinsatzAbschlussTests.Recorder<IWarningService> _hinweise = EinsatzAbschlussTests.Recorder<IWarningService>.Create((_, _) => null);
    private readonly Navigationspunkte _punkte;
    private readonly LeitungAuthStore _leitung;
    private readonly ElAppTokenService _elTokens;
    private readonly string _elToken;
    private Team _team = null!;
    private string _teamToken = null!;

    public NavigationspunkteTests()
    {
        Directory.CreateDirectory(_dir);
        _teamTokens = new TeamMobileTokenService(RandomNumberGenerator.GetBytes(64), _einsatz);
        _halsbaender = new CollarTrackingService(_einsatz);
        _punkte = new Navigationspunkte(_einsatz, _teamTokens, _hinweise.Proxy);
        _leitung = new LeitungAuthStore(_dir);
        Assert.Null(_leitung.Setup(_leitung.SetupCode!, "Rettungshund2026", "Rettungshund2026"));
        _elTokens = new ElAppTokenService(RandomNumberGenerator.GetBytes(64), _leitung);
        _elToken = _elTokens.Ausstellen(DateTime.UtcNow.AddHours(1));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task StartAsync()
    {
        await _einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = DateTime.Now.AddHours(-1) });
        _team = await _einsatz.AddTeamAsync(new Team { TeamName = "Team Rex", DogName = "Rex" });
        _teamToken = _teamTokens.IssueAppToken(_team.TeamId, DateTime.UtcNow.AddHours(24));
    }

    private TeamMobileAppController TeamController(string? token = null)
    {
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) => m.Name switch
        {
            nameof(ISettingsService.GetAppSettingsAsync) => Task.FromResult(new AppSettings()),
            nameof(ISettingsService.GetStaffelSettingsAsync) => Task.FromResult(new StaffelSettings { StaffelName = "RHS Test" }),
            _ => null
        });
        var c = new TeamMobileAppController(_teamTokens, _einsatz, settings.Proxy, NullLogger<TeamMobileAppController>.Instance,
            _halsbaender, new TeamAppEreignisse(_einsatz, _halsbaender, _teamTokens), new AppStatusmeldungen(), punkte: _punkte);
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer " + (token ?? _teamToken);
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private ElAppController ElController()
    {
        var c = new ElAppController(_elTokens, new ElPositionService(_einsatz), new LeitungsGeraetNamen(new VpnDeviceStore(_dir)),
            new PublicAccess(5002), NullLogger<ElAppController>.Instance, null,
            _leitung, new LoginLockout(), null, _einsatz, _halsbaender, null, null, null,
            new ElAppAktionen(), null, null, _teamTokens, navigationspunkte: _punkte);
        var http = new DefaultHttpContext();
        http.Connection.LocalPort = 5000;
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.9");
        http.Request.Headers.Authorization = "Bearer " + _elToken;
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static TeamMobileAppController.AppPunktRequest Punkt(string? text = "Fund: Jacke", double? lat = 49.3123456, double? lng = 8.4312344,
        string? id = null) => new() { Id = id ?? Guid.NewGuid().ToString(), Text = text, Lat = lat, Lng = lng, Genauigkeit = 8.4 };

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    private static int Code(IActionResult r) => ((ObjectResult)r).StatusCode ?? 200;

    // ── Team → EL ───────────────────────────────────────────────────────

    [Fact]
    public async Task Punkt_LegtKartenpunktNotizUndHinweisAn()
    {
        await StartAsync();

        var antwort = await TeamController().PostPunkt(Punkt());

        Assert.Equal(200, Code(antwort));
        var json = Json(antwort);
        Assert.False(json.GetProperty("doppelt").GetBoolean());
        var punkt = Assert.Single(_einsatz.CurrentEinsatz.MapMarkers);
        Assert.Equal(json.GetProperty("punktId").GetString(), punkt.Id);
        Assert.Equal("Team Rex: Fund: Jacke", punkt.Label);
        Assert.Equal(_team.TeamId, punkt.TeamId);
        Assert.Equal(Navigationspunkte.FarbeTeamPunkt, punkt.Color);
        Assert.Equal(49.312346, punkt.Latitude);
        Assert.Contains("±8 m", punkt.Description);
        Assert.False(string.IsNullOrEmpty(punkt.UtmZone));

        var notiz = Assert.Single(_einsatz.GlobalNotes, n => n.SourceType == TeamAppSuche.QuelleTeamApp);
        Assert.Equal("[Team Rex] Punkt gesetzt: Fund: Jacke (49.312346, 8.431234, ±8 m)", notiz.Text);
        Assert.Contains(nameof(IWarningService.AddWarning), _hinweise.Calls);
    }

    [Fact]
    public async Task Punkt_GleicheId_NurEinPunkt()
    {
        await StartAsync();
        var anfrage = Punkt(id: "wiederholt");

        var erste = Json(await TeamController().PostPunkt(anfrage));
        var zweite = Json(await TeamController().PostPunkt(anfrage));

        Assert.True(zweite.GetProperty("doppelt").GetBoolean());
        Assert.Equal(erste.GetProperty("punktId").GetString(), zweite.GetProperty("punktId").GetString());
        Assert.Single(_einsatz.CurrentEinsatz.MapMarkers);
        Assert.Single(_einsatz.GlobalNotes, n => n.SourceType == TeamAppSuche.QuelleTeamApp);
    }

    [Theory]
    [InlineData(null, 49.3, 8.4)]
    [InlineData("  ", 49.3, 8.4)]
    [InlineData("ok", 91.0, 8.4)]
    [InlineData("ok", 49.3, -181.0)]
    [InlineData("ok", null, 8.4)]
    [InlineData("ok", double.NaN, 8.4)]
    public async Task Punkt_UngueltigeAngaben_400(string? text, double? lat, double? lng)
    {
        await StartAsync();

        var antwort = await TeamController().PostPunkt(Punkt(text, lat, lng));

        Assert.Equal(400, Code(antwort));
        Assert.Empty(_einsatz.CurrentEinsatz.MapMarkers);
    }

    [Fact]
    public async Task Punkt_ZuLangerTextOderZeitNichtUtc_400()
    {
        await StartAsync();

        Assert.Equal(400, Code(await TeamController().PostPunkt(Punkt(new string('x', Navigationspunkte.MaxTextLaenge + 1)))));
        var lokal = Punkt();
        lokal.Zeit = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local);
        Assert.Equal(400, Code(await TeamController().PostPunkt(lokal)));
        Assert.Empty(_einsatz.CurrentEinsatz.MapMarkers);
    }

    [Fact]
    public async Task Punkt_GrenzeJeTeam_409()
    {
        await StartAsync();
        for (var i = 0; i < Navigationspunkte.MaxPunkteJeTeam; i++)
            Assert.Equal(200, Code(await TeamController().PostPunkt(Punkt($"Hinweis {i}"))));

        var antwort = await TeamController().PostPunkt(Punkt("einer zu viel"));

        Assert.Equal(409, Code(antwort));
        Assert.Equal(Navigationspunkte.MaxPunkteJeTeam, _einsatz.CurrentEinsatz.MapMarkers.Count);
    }

    [Fact]
    public async Task Punkt_UngueltigesToken_410()
    {
        await StartAsync();

        var antwort = await TeamController("falsch").PostPunkt(Punkt());

        Assert.Equal(410, Code(antwort));
        Assert.Empty(_einsatz.CurrentEinsatz.MapMarkers);
    }

    [Fact]
    public async Task Punkt_ElBekommtEreignisPunktMitText_UndKarteMitTeamId()
    {
        await StartAsync();
        using var ereignisse = new ElAppEreignisse(_einsatz, _halsbaender, new ElPositionService(_einsatz));
        var (_, leser) = ereignisse.Abonnieren(_elToken);

        var punktId = Json(await TeamController().PostPunkt(Punkt())).GetProperty("punktId").GetString();

        var alle = new List<string>();
        while (leser.TryRead(out var text)) alle.Add(text);
        var punkt = Assert.Single(alle, t => t.StartsWith("event: punkt", StringComparison.Ordinal));
        Assert.Contains($"\"id\":\"{punktId}\"", punkt);
        Assert.Contains("\"aenderung\":\"neu\"", punkt);
        Assert.Contains("\"text\":\"Team Rex: Fund: Jacke\"", punkt);
        Assert.Contains(alle, t => t.StartsWith("event: notiz", StringComparison.Ordinal));

        var karte = Json(ElController().GetKarte());
        var eintrag = Assert.Single(karte.GetProperty("punkte").EnumerateArray());
        Assert.Equal(_team.TeamId, eintrag.GetProperty("teamId").GetString());
        Assert.EndsWith("Z", eintrag.GetProperty("zeit").GetString());

        await _einsatz.RemoveMapMarkerAsync(punktId!);
        Assert.True(leser.TryRead(out var geloescht));
        Assert.Contains("\"aenderung\":\"geloescht\"", geloescht);
        Assert.Contains("\"text\":null", geloescht);
    }

    // ── EL → Team ───────────────────────────────────────────────────────

    [Fact]
    public async Task Ziel_AusElApp_KommtAlsMitteilungMitLinksInLage()
    {
        await StartAsync();
        var punkt = await _einsatz.AddMapMarkerAsync(new MapMarker { Label = "Wegkreuzung (Nord)", Latitude = 49.3123456, Longitude = 8.4312344 });

        var antwort = await ElController().TeamZiel(_team.TeamId, new ElAppController.ElAktion(Guid.NewGuid().ToString(), PunktId: punkt.Id));

        Assert.Equal(200, Code(antwort));
        var mitteilung = Assert.Single(_einsatz.TeamMitteilungen(_team.TeamId));
        Assert.Equal(Json(antwort).GetProperty("mitteilungId").GetString(), mitteilung.Id);
        Assert.Equal("Ziel: Wegkreuzung  Nord", mitteilung.Text);

        var lage = Json(await TeamController().GetLage());
        var ziel = Assert.Single(lage.GetProperty("mitteilungen").EnumerateArray()).GetProperty("ziel");
        Assert.Equal(punkt.Id, ziel.GetProperty("punktId").GetString());
        Assert.Equal(49.312346, ziel.GetProperty("lat").GetDouble());
        var links = ziel.GetProperty("links");
        Assert.Equal("geo:49.312346,8.431234?q=49.312346,8.431234(Wegkreuzung%20%20Nord)", links.GetProperty("geo").GetString());
        Assert.StartsWith("https://maps.apple.com/?ll=49.312346,8.431234", links.GetProperty("appleKarten").GetString());
        Assert.Equal("comgooglemaps://?daddr=49.312346,8.431234&directionsmode=walking", links.GetProperty("googleMaps").GetString());
        Assert.Equal("waze://?ll=49.312346,8.431234&navigate=yes", links.GetProperty("waze").GetString());
        Assert.Contains(_einsatz.GlobalNotes, n => n.Text.StartsWith("[Team Rex] Navigationsziel gesendet: Wegkreuzung  Nord"));
    }

    [Fact]
    public async Task Ziel_TeamAppBekommtMitteilungMitZielLive()
    {
        await StartAsync();
        using var ereignisse = new TeamAppEreignisse(_einsatz, _halsbaender, _teamTokens);
        var (_, leser) = ereignisse.Abonnieren(_team.TeamId);
        var punkt = await _einsatz.AddMapMarkerAsync(new MapMarker { Label = "Ziel A", Latitude = 49.3, Longitude = 8.4 });

        await _punkte.ZielSendenAsync(_team, punkt, "Test");

        var alle = new List<string>();
        while (leser.TryRead(out var text)) alle.Add(text);
        var mitteilung = Assert.Single(alle, t => t.StartsWith("event: mitteilung", StringComparison.Ordinal));
        Assert.Contains("\"ziel\":{\"punktId\":\"" + punkt.Id + "\"", mitteilung);
    }

    [Fact]
    public async Task Ziel_NormaleMitteilungHatZielNull()
    {
        await StartAsync();
        _teamTokens.BroadcastTeamMessage(_team.TeamId, "Bitte Funkkontakt");

        var lage = Json(await TeamController().GetLage());

        var eintrag = Assert.Single(lage.GetProperty("mitteilungen").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, eintrag.GetProperty("ziel").ValueKind);
    }

    [Fact]
    public async Task Ziel_UnbekannterPunktOderTeam_404()
    {
        await StartAsync();
        var punkt = await _einsatz.AddMapMarkerAsync(new MapMarker { Label = "A", Latitude = 49.3, Longitude = 8.4 });

        Assert.Equal(404, Code(await ElController().TeamZiel(_team.TeamId, new ElAppController.ElAktion(Guid.NewGuid().ToString(), PunktId: "gibtsnicht"))));
        Assert.Equal(404, Code(await ElController().TeamZiel("gibtsnicht", new ElAppController.ElAktion(Guid.NewGuid().ToString(), PunktId: punkt.Id))));
        Assert.Empty(_einsatz.TeamMitteilungen(_team.TeamId));
    }

    // ── Bericht und Löschfrist ──────────────────────────────────────────

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task Abschluss_PunkteNurMitAuswahlImArchiv(bool punkteImBericht, int erwartet)
    {
        await StartAsync();
        await _einsatz.AddMapMarkerAsync(new MapMarker { Label = "A", Latitude = 49.3, Longitude = 8.4 });
        var archiv = EinsatzAbschlussTests.Recorder<IArchivService>.Create((m, args) =>
            m.Name == nameof(IArchivService.ArchiveEinsatzAsync)
                ? Task.FromResult(ArchivedEinsatz.FromEinsatzData((EinsatzData)args![0]!, (string)args[1]!, (string)args[2]!))
                : null);
        var pdf = EinsatzAbschlussTests.Recorder<IPdfExportService>.Create((_, _) => Task.FromResult(new PdfExportResult { Success = true }));
        var zeit = EinsatzAbschlussTests.Recorder<ITimeService>.Create((m, _) => m.Name == "get_Now" ? DateTime.Now : null);
        var service = new EinsatzAbschlussService(_einsatz, archiv.Proxy, pdf.Proxy, zeit.Proxy);

        var ergebnis = await service.AbschliessenAsync(new EinsatzAbschlussAnfrage(
            _einsatz.CurrentEinsatz.EinsatzDatum, "Uebung abgeschlossen", "", null, false, [], [], PunkteImBericht: punkteImBericht));

        Assert.Equal(erwartet, ergebnis.Archiviert.MapMarkers.Count);
    }

    [Fact]
    public void Loeschfrist_NotizenEntferntAuchKartenpunkte()
    {
        var archiviert = new ArchivedEinsatz { MapMarkers = [new MapMarker { Label = "A" }] };

        Loeschfristen.Anwenden(archiviert, [Loeschfristen.Notizen], DateTime.Now);

        Assert.Empty(archiviert.MapMarkers);
    }
}

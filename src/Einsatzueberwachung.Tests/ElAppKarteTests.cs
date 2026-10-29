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

/// <summary>Native Einsatzleitung, Stufe 4b: Karte, Tracks, Halsbaender (#125)</summary>
public sealed class ElAppKarteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-elkarte-" + Guid.NewGuid().ToString("N"));
    private readonly LeitungAuthStore _leitung;
    private readonly EinsatzService _einsatz = new();
    private readonly CollarTrackingService _halsbaender;
    private readonly ElPositionService _positionen;
    private readonly ElAppTokenService _tokens;
    private readonly string _token;

    private sealed class Uhr : ITimeService
    {
        public DateTime Now => Ortszeit.Jetzt;
        public void Refresh() { }
    }

    public ElAppKarteTests()
    {
        Directory.CreateDirectory(_dir);
        _leitung = new LeitungAuthStore(_dir);
        Assert.Null(_leitung.Setup(_leitung.SetupCode!, "Rettungshund2026", "Rettungshund2026"));
        _halsbaender = new CollarTrackingService(_einsatz);
        _positionen = new ElPositionService(_einsatz);
        _tokens = new ElAppTokenService(RandomNumberGenerator.GetBytes(64), _leitung);
        _token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(1));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ElAppController Controller(int port = 5000, string? token = null)
    {
        var c = new ElAppController(_tokens, _positionen, new LeitungsGeraetNamen(new VpnDeviceStore(_dir)),
            new PublicAccess(5002), NullLogger<ElAppController>.Instance, null,
            _leitung, new LoginLockout(), null, _einsatz, _halsbaender, null, null, null,
            abdeckungen: new LiveAbdeckungService(_einsatz, _halsbaender));
        var http = new DefaultHttpContext();
        http.Connection.LocalPort = port;
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.9");
        http.Request.Headers.Authorization = "Bearer " + (token ?? _token);
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    private async Task<Team> AufbauAsync()
    {
        await _einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 29, 9, 0, 0) });
        var team = await _einsatz.AddTeamAsync(new Team { TeamName = "Team Rex" });
        var gebiet = await _einsatz.AddSearchAreaAsync(new SearchArea { Name = "Gebiet A", Color = "#2f6fd6", Coordinates = [(49.31, 8.43), (49.32, 8.43), (49.32, 8.44)] });
        await _einsatz.AssignTeamToSearchAreaAsync(gebiet.Id, team.TeamId);
        await _halsbaender.ReceiveLocationAsync("HB-7", "Halsband 7", 49.315, 8.435, 72, DateTime.UtcNow);
        await _halsbaender.ReceiveLocationAsync("HB-8", "Halsband 8", 49.316, 8.436, 50, DateTime.UtcNow);
        return team;
    }

    [Fact]
    public async Task Karte_GebieteUndAllePositionsarten()
    {
        var team = await AufbauAsync();
        await _halsbaender.AssignCollarToTeamAsync("HB-7", team.TeamId);
        team.StartTimer();
        await _einsatz.UpdateTeamPhoneLocationAsync(team.TeamId, 49.312, 8.432, timestampUtc: DateTime.UtcNow);
        _positionen.Melden("iPhone Anna", [(49.30, 8.42, 5.0, DateTime.UtcNow, 64)]);
        await _einsatz.SetElwPositionAsync(49.29, 8.41);

        var karte = Json(Controller().GetKarte());

        var gebiet = Assert.Single(karte.GetProperty("suchgebiete").EnumerateArray());
        Assert.Equal(team.TeamId, gebiet.GetProperty("teamId").GetString());
        Assert.Equal(3, gebiet.GetProperty("punkte").GetArrayLength());
        var arten = karte.GetProperty("positionen").EnumerateArray().Select(p => p.GetProperty("art").GetString()).ToList();
        Assert.Equal(2, arten.Count(a => a == "halsband"));
        Assert.Contains("handy", arten);
        Assert.Contains("el", arten);
        Assert.Contains("elw", arten);
        var hb7 = karte.GetProperty("positionen").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "HB-7");
        Assert.Equal(team.TeamId, hb7.GetProperty("teamId").GetString());
        Assert.EndsWith("Z", hb7.GetProperty("zeit").GetString());
    }

    [Fact]
    public async Task Tracks_LaufendJeTeam_MitSeitNurNeue_AbgeschlossenMitTeamId()
    {
        var team = await AufbauAsync();
        await _halsbaender.AssignCollarToTeamAsync("HB-7", team.TeamId);
        var suche = new TeamSucheService(_einsatz, _halsbaender, new Uhr());
        await suche.StartAsync(team.TeamId);
        var alt = DateTime.UtcNow.AddSeconds(-30);
        await _halsbaender.ReceiveLocationAsync("HB-7", "Halsband 7", 49.317, 8.437, 70, alt);
        await _halsbaender.ReceiveLocationAsync("HB-7", "Halsband 7", 49.318, 8.438, 70, DateTime.UtcNow);

        var laufend = Assert.Single(Json(Controller().GetTracks(null)).GetProperty("laufend").EnumerateArray());
        Assert.Equal(team.TeamId, laufend.GetProperty("teamId").GetString());
        Assert.Equal(2, laufend.GetProperty("hund").GetArrayLength());
        var nurNeue = Json(Controller().GetTracks(DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds()));
        Assert.Equal(1, nurNeue.GetProperty("laufend")[0].GetProperty("hund").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, nurNeue.GetProperty("abgeschlossen").ValueKind);

        await suche.StopAsync(team.TeamId);
        var danach = Json(Controller().GetTracks(null));
        Assert.Empty(danach.GetProperty("laufend").EnumerateArray());
        Assert.Equal(team.TeamId, Assert.Single(danach.GetProperty("abgeschlossen").EnumerateArray()).GetProperty("teamId").GetString());
    }

    [Fact]
    public async Task Halsbaender_FreiUndZuletztBenutzt_Zugang()
    {
        var team = await AufbauAsync();
        await _halsbaender.AssignCollarToTeamAsync("HB-7", team.TeamId);
        var suche = new TeamSucheService(_einsatz, _halsbaender, new Uhr());
        await suche.StartAsync(team.TeamId);
        await _halsbaender.ReceiveLocationAsync("HB-7", "Halsband 7", 49.317, 8.437, 70, DateTime.UtcNow);
        await suche.StopAsync(team.TeamId); // gibt HB-7 frei, Track gespeichert

        var antwort = Json(Controller().GetHalsbaender());
        Assert.Equal(["HB-7", "HB-8"], antwort.GetProperty("frei").EnumerateArray().Select(h => h.GetProperty("id").GetString()).ToArray());
        var zuletzt = Assert.Single(antwort.GetProperty("zuletzt").EnumerateArray());
        Assert.Equal("HB-7", zuletzt.GetProperty("halsbandId").GetString());

        Assert.IsType<NotFoundResult>(Controller(port: 5002).GetHalsbaender());
        Assert.Equal(410, ((ObjectResult)Controller(token: "e2.falsch").GetKarte()).StatusCode);
    }
}

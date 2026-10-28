using System.Security.Cryptography;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Schnittstelle der Tracking-App, Rolle Team (#110, docs/APP_API.md)</summary>
public class TrackingAppTests
{
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(64);

    private static async Task<(EinsatzService Einsatz, TeamMobileTokenService Tokens, Team Team)> LaufendesTeamAsync()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", Stichwort = "Vermisste Person", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 28, 9, 0, 0) });
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Team Rex" });
        team.StartTimer();
        return (einsatz, new TeamMobileTokenService(Secret, einsatz), team);
    }

    private static TeamMobileAppController Controller(EinsatzService einsatz, TeamMobileTokenService tokens, string? token)
    {
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) => m.Name switch
        {
            nameof(ISettingsService.GetAppSettingsAsync) => Task.FromResult(new AppSettings()),
            nameof(ISettingsService.GetStaffelSettingsAsync) => Task.FromResult(new StaffelSettings { StaffelName = "RHS Test" }),
            _ => null
        });
        var controller = new TeamMobileAppController(tokens, einsatz, settings.Proxy, NullLogger<TeamMobileAppController>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("einsatz.example.org");
        if (token is not null) http.Request.Headers.Authorization = "Bearer " + token;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static string Modus(IActionResult result) =>
        JsonSerializer.SerializeToElement(((ObjectResult)result).Value).GetProperty("mode").GetString()!;

    [Fact]
    public async Task AppToken_GiltNurImEinsatzUndBisZumAblauf()
    {
        var (einsatz, tokens, team) = await LaufendesTeamAsync();
        var jetzt = DateTime.UtcNow;
        var token = tokens.IssueAppToken(team.TeamId, jetzt.AddHours(24));

        Assert.True(tokens.TryValidateAppToken(token, jetzt, out var teamId, out var ablauf));
        Assert.Equal(team.TeamId, teamId);
        Assert.InRange(ablauf, jetzt.AddHours(24).AddSeconds(-1), jetzt.AddHours(24));

        Assert.False(tokens.TryValidateAppToken(token, jetzt.AddHours(25), out _, out _));           // abgelaufen
        Assert.False(tokens.TryValidateAppToken(token[..^2] + "xx", jetzt, out _, out _));            // Signatur
        Assert.False(tokens.TryValidateAppToken(tokens.IssueTeamCookieValue(team.TeamId), jetzt, out _, out _)); // Cookie ≠ App-Token
        Assert.False(tokens.TryValidateTeamCookie(token, out _));                                      // App-Token ≠ Cookie

        await einsatz.EndEinsatzAsync();
        Assert.False(tokens.TryValidateAppToken(token, jetzt, out _, out _));                          // Einsatz vorbei
    }

    [Fact]
    public async Task Modus_FolgtDerSuche()
    {
        var (einsatz, _, team) = await LaufendesTeamAsync();
        Assert.Equal(OrtungsModus.Track, OrtungsSteuerung.FuerTeam(einsatz.CurrentEinsatz, team));

        team.PauseTimer();
        Assert.Equal(OrtungsModus.Pause, OrtungsSteuerung.FuerTeam(einsatz.CurrentEinsatz, team));

        team.StopTimer();
        Assert.Equal(OrtungsModus.Pause, OrtungsSteuerung.FuerTeam(einsatz.CurrentEinsatz, team)); // neue Suche moeglich

        team.StartTimer();
        Assert.Equal(OrtungsModus.Track, OrtungsSteuerung.FuerTeam(einsatz.CurrentEinsatz, team));

        Assert.Equal(OrtungsModus.Stop, OrtungsSteuerung.FuerTeam(einsatz.CurrentEinsatz, null));
        await einsatz.EndEinsatzAsync();
        Assert.Equal(OrtungsModus.Stop, OrtungsSteuerung.FuerTeam(einsatz.CurrentEinsatz, team));
    }

    [Fact]
    public async Task Config_LiefertTeamEinsatzUndAnweisung()
    {
        var (einsatz, tokens, team) = await LaufendesTeamAsync();
        var token = tokens.IssueAppToken(team.TeamId, DateTime.UtcNow.AddHours(24));

        var json = JsonSerializer.SerializeToElement(((ObjectResult)await Controller(einsatz, tokens, token).GetConfig()).Value);

        Assert.Equal(1, json.GetProperty("apiVersion").GetInt32());
        Assert.Equal("Team Rex", json.GetProperty("teamName").GetString());
        Assert.Equal("RHS Test", json.GetProperty("staffel").GetString());
        Assert.Equal("Vermisste Person · Testwald", json.GetProperty("einsatz").GetString());
        Assert.Equal("track", json.GetProperty("mode").GetString());
        Assert.Equal("https://einsatz.example.org/api/team-mobile/app/v1/positions", json.GetProperty("positionUrl").GetString());
        Assert.Equal(20, json.GetProperty("intervalSec").GetInt32());
    }

    [Fact]
    public async Task Positionen_AusDemPuffer_KommenMitOriginalzeit()
    {
        var (einsatz, tokens, team) = await LaufendesTeamAsync();
        var token = tokens.IssueAppToken(team.TeamId, DateTime.UtcNow.AddHours(24));
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        var request = new TeamMobileAppController.AppPositionenRequest
        {
            Positions =
            [
                new() { Lat = 49.31, Lng = 8.43, Accuracy = 8, Timestamp = t0 + 20_000, Battery = 71 },
                new() { Lat = 49.30, Lng = 8.42, Accuracy = 9, Timestamp = t0, Battery = 72 }, // Funkloch: aelter, kommt spaeter
                new() { Lat = 999, Lng = 8.42, Timestamp = t0 + 40_000 }                        // unsinnig → ignoriert
            ]
        };

        var result = await Controller(einsatz, tokens, token).PostPositions(request);

        Assert.Equal("track", Modus(result));
        Assert.Equal(2, JsonSerializer.SerializeToElement(((ObjectResult)result).Value).GetProperty("accepted").GetInt32());
        var verlauf = einsatz.GetPhoneTrackHistory(team.TeamId);
        Assert.Equal(2, verlauf.Count);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(t0).UtcDateTime, Ortszeit.NachUtc(verlauf[0].Timestamp), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task NachEinsatzende_Oder_OhneToken_Stop410()
    {
        var (einsatz, tokens, team) = await LaufendesTeamAsync();
        var token = tokens.IssueAppToken(team.TeamId, DateTime.UtcNow.AddHours(24));

        var ohne = await Controller(einsatz, tokens, null).GetStatus();
        Assert.Equal(StatusCodes.Status410Gone, ((ObjectResult)ohne).StatusCode);

        await einsatz.EndEinsatzAsync();
        var nachEnde = await Controller(einsatz, tokens, token).PostPositions(new() { Positions = [new() { Lat = 49.3, Lng = 8.4 }] });

        Assert.Equal(StatusCodes.Status410Gone, ((ObjectResult)nachEnde).StatusCode);
        Assert.Equal("stop", Modus(nachEnde));
    }

    [Fact]
    public async Task ZuVielePositionen_WerdenAbgelehnt()
    {
        var (einsatz, tokens, team) = await LaufendesTeamAsync();
        var token = tokens.IssueAppToken(team.TeamId, DateTime.UtcNow.AddHours(1));
        var request = new TeamMobileAppController.AppPositionenRequest
        {
            Positions = Enumerable.Range(0, TeamMobileAppController.MaxPositionenProUpload + 1)
                .Select(_ => new TeamMobileAppController.AppPosition { Lat = 49.3, Lng = 8.4 }).ToList()
        };

        Assert.IsType<BadRequestObjectResult>(await Controller(einsatz, tokens, token).PostPositions(request));
    }

    private static TeamMobileAppController.AppLoginRequest Anmeldung(string? code, string? teamId = null) =>
        new() { Code = code, TeamId = teamId };

    [Fact]
    public async Task NativeAnmeldung_CodeLiefertTeamsUndTokenFuerConfig()
    {
        var (einsatz, tokens, team) = await LaufendesTeamAsync();
        var controller = Controller(einsatz, tokens, null);

        var liste = JsonSerializer.SerializeToElement(((ObjectResult)await controller.LoginTeams(Anmeldung(tokens.CurrentMasterToken))).Value);
        Assert.Equal("Vermisste Person · Testwald", liste.GetProperty("einsatz").GetString());
        Assert.Equal("RHS Test", liste.GetProperty("staffel").GetString());
        var eintrag = Assert.Single(liste.GetProperty("teams").EnumerateArray());
        Assert.Equal(team.TeamId, eintrag.GetProperty("teamId").GetString());
        Assert.Equal("Team Rex", eintrag.GetProperty("teamName").GetString());

        // Code wie auf der Anmeldeseite: Leerzeichen und Kleinschreibung egal
        var code = string.Join(" ", tokens.CurrentMasterToken!.ToLowerInvariant().Chunk(4).Select(c => new string(c)));
        var antwort = JsonSerializer.SerializeToElement(((ObjectResult)controller.Login(Anmeldung(code, team.TeamId))).Value);
        Assert.Equal("team", antwort.GetProperty("rolle").GetString());
        Assert.Equal(team.TeamId, antwort.GetProperty("teamId").GetString());
        var token = antwort.GetProperty("token").GetString()!;
        Assert.True(tokens.TryValidateAppToken(token, DateTime.UtcNow, out _, out var ablauf));
        Assert.InRange(ablauf, DateTime.UtcNow.AddHours(23.9), DateTime.UtcNow.AddHours(24));

        var config = JsonSerializer.SerializeToElement(((ObjectResult)await Controller(einsatz, tokens, token).GetConfig()).Value);
        Assert.Equal("Team Rex", config.GetProperty("teamName").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FALSCHERCODE2345")]
    public async Task NativeAnmeldung_FalscherCodeWirdAbgelehnt(string? code)
    {
        var (einsatz, tokens, team) = await LaufendesTeamAsync();
        var controller = Controller(einsatz, tokens, null);
        Assert.IsType<UnauthorizedObjectResult>(await controller.LoginTeams(Anmeldung(code)));
        Assert.IsType<UnauthorizedObjectResult>(controller.Login(Anmeldung(code, team.TeamId)));
    }

    [Fact]
    public async Task NativeAnmeldung_UnbekanntesTeam()
    {
        var (einsatz, tokens, _) = await LaufendesTeamAsync();
        Assert.IsType<NotFoundObjectResult>(Controller(einsatz, tokens, null).Login(Anmeldung(tokens.CurrentMasterToken, "gibt-es-nicht")));
        Assert.IsType<NotFoundObjectResult>(Controller(einsatz, tokens, null).Login(Anmeldung(tokens.CurrentMasterToken, null)));
    }

    [Fact]
    public async Task NativeAnmeldung_NachEinsatzendeNichtMehr()
    {
        var (einsatz, tokens, team) = await LaufendesTeamAsync();
        var code = tokens.CurrentMasterToken;
        await einsatz.EndEinsatzAsync();
        var controller = Controller(einsatz, tokens, null);
        Assert.IsType<UnauthorizedObjectResult>(await controller.LoginTeams(Anmeldung(code)));
        Assert.IsType<UnauthorizedObjectResult>(controller.Login(Anmeldung(code, team.TeamId)));
    }
}

/// <summary>Handy-Tab der Karte: Handy-Daten eines Teams entfernen</summary>
public class HandyDatenEntfernenTests
{
    [Fact]
    public async Task NichtWaehrendDerSuche_DanachLetztePositionUndVerlaufWeg()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 28, 9, 0, 0) });
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Team Luna" });
        team.StartTimer();
        await einsatz.UpdateTeamPhoneLocationAsync(team.TeamId, 49.3, 8.4);
        var gemeldet = new List<string>();
        einsatz.TeamPhoneLocationRemoved += gemeldet.Add;

        Assert.False(einsatz.EntferneHandyDaten(team.TeamId)); // Suche laeuft
        Assert.NotEmpty(einsatz.GetPhoneTrackHistory(team.TeamId));

        team.StopTimer();
        Assert.True(einsatz.EntferneHandyDaten(team.TeamId));
        Assert.Empty(einsatz.GetPhoneTrackHistory(team.TeamId));
        Assert.False(einsatz.PhoneLocations.ContainsKey(team.TeamId));
        Assert.Contains(team.TeamId, gemeldet); // Karten und Speicherung erfahren davon

        Assert.True(einsatz.EntferneHandyDaten("geloeschtes-team")); // Reste geloeschter Teams lassen sich entfernen
    }
}

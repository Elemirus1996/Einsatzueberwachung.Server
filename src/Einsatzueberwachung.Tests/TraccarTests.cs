using System.Security.Cryptography;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Einsatzueberwachung.Tests;

// ------------------------------------------------------------
// 1.10 – Hintergrund-GPS der Hundeführer über die App „Traccar Client“
// ------------------------------------------------------------
public class TraccarProtocolTests
{
    private static Func<string, string?> Values(Dictionary<string, string> values) =>
        key => values.TryGetValue(key, out var v) ? v : null;

    [Fact]
    public void Formular_WieVonTraccarClient_WirdGelesen()
    {
        // Felder wie HttpUploader.kt im Traccar-SDK: id, lat, lon, timestamp (Sekunden), accuracy, batt, speed
        var position = TraccarProtocol.Parse(Values(new()
        {
            ["id"] = "team.123.sig", ["lat"] = "51.5136", ["lon"] = "7.4653", ["timestamp"] = "1792836000",
            ["accuracy"] = "4.5", ["batt"] = "81.0", ["speed"] = "0.0",
        }));

        Assert.NotNull(position);
        Assert.Equal("team.123.sig", position!.DeviceId);
        Assert.Equal(51.5136, position.Latitude);
        Assert.Equal(7.4653, position.Longitude);
        Assert.Equal(4.5, position.Accuracy);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792836000).UtcDateTime, position.TimestampUtc);
    }

    [Theory]
    [InlineData("1792836000", 1792836000000)]
    [InlineData("1792836000123", 1792836000123)]
    [InlineData("2026-10-25T18:40:00Z", 1792953600000)]
    [InlineData("2026-10-25 18:40:00", 1792953600000)]
    public void Zeitstempel_InAllenVariantenDesProtokolls(string raw, long expectedUnixMs) =>
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(expectedUnixMs).UtcDateTime, TraccarProtocol.ParseTimestamp(raw));

    [Fact]
    public void LocationParameter_UndDeviceId_WerdenAuchVerstanden()
    {
        var position = TraccarProtocol.Parse(Values(new() { ["deviceid"] = "x", ["location"] = "51.1,7.2" }));
        Assert.Equal((51.1, 7.2), (position!.Latitude, position.Longitude));
        Assert.Null(position.TimestampUtc);
    }

    [Theory]
    [InlineData(null, "51.1", "7.2")]
    [InlineData("x", "abc", "7.2")]
    [InlineData("x", "91", "7.2")]
    [InlineData("x", "51.1", "181")]
    [InlineData("x", "NaN", "7.2")]
    public void UngueltigeAngaben_WerdenAbgelehnt(string? id, string lat, string lon)
    {
        var values = new Dictionary<string, string> { ["lat"] = lat, ["lon"] = lon };
        if (id is not null) values["id"] = id;
        Assert.Null(TraccarProtocol.Parse(Values(values)));
    }
}

public class TraccarEndpointTests
{
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(64);

    private static async Task<(EinsatzService Einsatz, TeamMobileTokenService Tokens, Team Team)> RunningTeamAsync()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 25, 9, 0, 0) });
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Team 1" });
        team.StartTimer();
        return (einsatz, new TeamMobileTokenService(Secret, einsatz), team);
    }

    private static TeamMobileController Controller(EinsatzService einsatz, TeamMobileTokenService tokens, Dictionary<string, StringValues> form)
    {
        var controller = new TeamMobileController(tokens, einsatz, new CollarTrackingService(einsatz), NullLogger<TeamMobileController>.Instance);
        var http = new DefaultHttpContext();
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Form = new FormCollection(form);
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static Dictionary<string, StringValues> Form(string id, double lat, long? timestamp = null)
    {
        var form = new Dictionary<string, StringValues>
        {
            ["id"] = id, ["lat"] = lat.ToString(System.Globalization.CultureInfo.InvariantCulture), ["lon"] = "7.0",
        };
        if (timestamp is { } ts) form["timestamp"] = ts.ToString();
        return form;
    }

    [Fact]
    public async Task GueltigesTeamToken_SpeichertPositionImLaufweg()
    {
        var (einsatz, tokens, team) = await RunningTeamAsync();
        var token = tokens.IssueTeamCookieValue(team.TeamId);

        var result = await Controller(einsatz, tokens, Form(token, 51.1)).PostTraccarPosition();

        Assert.IsType<OkResult>(result);
        Assert.Single(einsatz.GetPhoneTrackHistory(team.TeamId));
        team.StopTimer();
    }

    [Fact]
    public async Task NachgesendetePositionen_WerdenZeitlichEinsortiert()
    {
        var (einsatz, tokens, team) = await RunningTeamAsync();
        var token = tokens.IssueTeamCookieValue(team.TeamId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await Controller(einsatz, tokens, Form(token, 51.3, now)).PostTraccarPosition();
        await Controller(einsatz, tokens, Form(token, 51.1, now - 120)).PostTraccarPosition();
        await Controller(einsatz, tokens, Form(token, 51.2, now - 60)).PostTraccarPosition();

        Assert.Equal([51.1, 51.2, 51.3], einsatz.GetPhoneTrackHistory(team.TeamId).Select(l => l.Latitude));
        team.StopTimer();
    }

    [Fact]
    public async Task TokenNachEinsatzende_WirdAngenommenAberVerworfen()
    {
        var (einsatz, tokens, team) = await RunningTeamAsync();
        var token = tokens.IssueTeamCookieValue(team.TeamId);
        await einsatz.EndEinsatzAsync();

        var result = await Controller(einsatz, tokens, Form(token, 51.1)).PostTraccarPosition();

        // 200, damit die App ihren Puffer nicht endlos wiederholt – gespeichert wird nichts
        Assert.IsType<OkResult>(result);
        Assert.Empty(einsatz.GetPhoneTrackHistory(team.TeamId));
    }

    [Fact]
    public async Task GefaelschteId_WirdVerworfen()
    {
        var (einsatz, tokens, team) = await RunningTeamAsync();

        await Controller(einsatz, tokens, Form(team.TeamId, 51.1)).PostTraccarPosition();
        await Controller(einsatz, tokens, Form($"{team.TeamId}.1.gefaelscht", 51.1)).PostTraccarPosition();

        Assert.Empty(einsatz.GetPhoneTrackHistory(team.TeamId));
        team.StopTimer();
    }

    [Fact]
    public async Task OhneKoordinaten_400()
    {
        var (einsatz, tokens, team) = await RunningTeamAsync();
        var form = new Dictionary<string, StringValues> { ["id"] = tokens.IssueTeamCookieValue(team.TeamId) };

        Assert.IsType<BadRequestObjectResult>(await Controller(einsatz, tokens, form).PostTraccarPosition());
        team.StopTimer();
    }
}

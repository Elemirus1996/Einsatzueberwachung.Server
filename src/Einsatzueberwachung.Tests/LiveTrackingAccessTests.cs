using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Einsatzueberwachung.Server.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Einsatzueberwachung.Tests;

// ------------------------------------------------------------
// 1.10 – Anbindung der LiveTracking-App (API-Schlüssel, Verbindungscode, Puffer)
// ------------------------------------------------------------
public sealed class LiveTrackingAccessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-livetracking-" + Guid.NewGuid().ToString("N"));

    public LiveTrackingAccessTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Schluessel_WirdErzeugt_UndUeberstehtNeustart()
    {
        var first = new LiveTrackingAccess(_dir);
        Assert.True(first.ApiKey.Length >= 32);

        var afterRestart = new LiveTrackingAccess(_dir);
        Assert.Equal(first.ApiKey, afterRestart.ApiKey);
        Assert.True(afterRestart.IsValid(first.ApiKey));
    }

    [Fact]
    public void NeuerSchluessel_MachtDenAltenUngueltig()
    {
        var access = new LiveTrackingAccess(_dir);
        var old = access.ApiKey;

        access.Regenerate();

        Assert.False(access.IsValid(old));
        Assert.True(access.IsValid(access.ApiKey));
        Assert.False(access.IsValid(null));
        Assert.False(access.IsValid(""));
    }

    [Fact]
    public void Verbindungscode_EnthaeltAdresseSchluesselUndFingerabdruck()
    {
        var access = new LiveTrackingAccess(_dir);

        var code = access.BuildConnectionCode("https://10.10.0.1/", "AB12CD");
        var info = LiveTrackingAccess.ParseConnectionCode(code);

        Assert.StartsWith(LiveTrackingAccess.CodePrefix, code);
        Assert.DoesNotContain("=", code);
        Assert.NotNull(info);
        Assert.Equal("https://10.10.0.1", info!.ServerUrl);
        Assert.Equal(access.ApiKey, info.ApiKey);
        Assert.Equal("AB12CD", info.CertificateSha256);
    }

    [Theory]
    [InlineData("https://10.10.0.1")]
    [InlineData("EUL1.!!!kein-base64")]
    [InlineData("EUL1.")]
    public void UngueltigerVerbindungscode_WirdErkannt(string code) =>
        Assert.Null(LiveTrackingAccess.ParseConnectionCode(code));

    [Fact]
    public void Webhook_OhneOderMitFalschemSchluessel_401_MitRichtigem_Durchgelassen()
    {
        var access = new LiveTrackingAccess(_dir);
        var filter = new RequireLiveTrackingKeyAttribute();

        Assert.IsType<UnauthorizedObjectResult>(Run(filter, access, null));
        Assert.IsType<UnauthorizedObjectResult>(Run(filter, access, "falsch"));
        Assert.Null(Run(filter, access, access.ApiKey));
    }

    private static IActionResult? Run(RequireLiveTrackingKeyAttribute filter, LiveTrackingAccess access, string? key)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(access).BuildServiceProvider() };
        if (key is not null) http.Request.Headers[LiveTrackingAccess.HeaderName] = key;
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        filter.OnAuthorization(context);
        return context.Result;
    }
}

public class BufferedCollarLocationTests
{
    private static CollarWebhookController.CollarLocationRequest Request(string id, double lat, DateTimeOffset? timestamp) =>
        new(id, "Rex", new CollarWebhookController.CoordinatesDto(lat, 7.0), 80, timestamp);

    [Fact]
    public async Task NachgesendetePosition_UeberschreibtNichtDieAktuelle_UndWirdImTrackEinsortiert()
    {
        var einsatz = new EinsatzService();
        var tracking = new CollarTrackingService(einsatz);
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Team 1" });
        await tracking.AssignCollarToTeamAsync("C1", team.TeamId);
        team.StartTimer();

        var now = DateTime.UtcNow;
        await tracking.ReceiveLocationAsync("C1", "Rex", 51.3, 7.0, timestampUtc: now);
        // Aus dem Puffer der App, zwei Minuten älter
        await tracking.ReceiveLocationAsync("C1", "Rex", 51.1, 7.0, timestampUtc: now.AddMinutes(-2));
        await tracking.ReceiveLocationAsync("C1", "Rex", 51.2, 7.0, timestampUtc: now.AddMinutes(-1));

        Assert.Equal(51.3, tracking.GetLatestLocation("C1")!.Latitude);
        Assert.Equal([51.1, 51.2, 51.3], tracking.GetLocationHistory("C1").Select(l => l.Latitude));
        team.StopTimer();
    }

    [Fact]
    public async Task NachgesendetePosition_LoestKeineLiveMeldungAus()
    {
        var tracking = new CollarTrackingService(new EinsatzService());
        var events = 0;
        tracking.CollarLocationReceived += (_, _) => events++;

        await tracking.ReceiveLocationAsync("C2", "Rex", 51.3, 7.0, timestampUtc: DateTime.UtcNow);
        await tracking.ReceiveLocationAsync("C2", "Rex", 51.1, 7.0, timestampUtc: DateTime.UtcNow.AddMinutes(-5));

        Assert.Equal(1, events);
    }

    [Fact]
    public async Task Webhook_UebernimmtZeitstempel_BegrenztZukunft_VerwirftUeber24Stunden()
    {
        var tracking = new CollarTrackingService(new EinsatzService());
        var controller = new CollarWebhookController(tracking);
        var past = DateTimeOffset.UtcNow.AddMinutes(-10);

        Assert.IsType<OkObjectResult>(await controller.ReceiveLocation(Request("C3", 51.0, past)));
        Assert.Equal(past.UtcDateTime, tracking.GetLatestLocation("C3")!.Timestamp, TimeSpan.FromMilliseconds(1));

        Assert.IsType<OkObjectResult>(await controller.ReceiveLocation(Request("C4", 51.0, DateTimeOffset.UtcNow.AddHours(3))));
        Assert.True(tracking.GetLatestLocation("C4")!.Timestamp <= DateTime.UtcNow);

        Assert.IsType<BadRequestObjectResult>(await controller.ReceiveLocation(Request("C5", 51.0, DateTimeOffset.UtcNow.AddHours(-25))));
    }
}

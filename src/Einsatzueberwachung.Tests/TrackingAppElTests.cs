using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Tracking-App, Rolle Einsatzleitung: Standort fuer den Fuehrungsassistenten, nur im VPN (#110)</summary>
public sealed class TrackingAppElTests : IDisposable
{
    private const int OeffentlicherPort = 5002;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-elapp-" + Guid.NewGuid().ToString("N"));
    private readonly LeitungAuthStore _leitung;
    private readonly ElAppTokenService _tokens;
    private readonly LeitungsGeraetNamen _geraete;

    public TrackingAppElTests()
    {
        Directory.CreateDirectory(_dir);
        _leitung = new LeitungAuthStore(_dir);
        Assert.Null(_leitung.Setup(_leitung.SetupCode!, "Rettungshund2026", "Rettungshund2026"));
        _tokens = new ElAppTokenService(RandomNumberGenerator.GetBytes(64), _leitung);
        var vpn = new VpnDeviceStore(_dir);
        vpn.Add(new VpnDevice { Name = "Tablet EL", Ip = "10.10.0.5", PublicKey = "k1" });
        _geraete = new LeitungsGeraetNamen(vpn);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static async Task<EinsatzService> LaufenderEinsatzAsync()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 28, 9, 0, 0) });
        return einsatz;
    }

    private ElAppController Controller(ElPositionService positionen, string? token, int lokalerPort = 5000)
    {
        var controller = new ElAppController(_tokens, positionen, _geraete, new PublicAccess(OeffentlicherPort), NullLogger<ElAppController>.Instance);
        var http = new DefaultHttpContext();
        http.Connection.LocalPort = lokalerPort;
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.5");
        if (token is not null) http.Request.Headers.Authorization = "Bearer " + token;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static string Modus(IActionResult r) =>
        JsonSerializer.SerializeToElement(((ObjectResult)r).Value).GetProperty("mode").GetString()!;

    private static TeamMobileAppController.AppPositionenRequest Punkte(params (double Lat, double Lng, long Ms)[] p) =>
        new() { Positions = p.Select(x => new TeamMobileAppController.AppPosition { Lat = x.Lat, Lng = x.Lng, Timestamp = x.Ms, Battery = 64 }).ToList() };

    [Fact]
    public void Token_PasswortaenderungUndAblaufBeendenIhn()
    {
        var jetzt = DateTime.UtcNow;
        var token = _tokens.Ausstellen(jetzt.AddHours(24));

        Assert.True(_tokens.TryPruefen(token, jetzt, out _));
        Assert.False(_tokens.TryPruefen(token, jetzt.AddHours(25), out _));
        Assert.False(_tokens.TryPruefen(token[..^2] + "xx", jetzt, out _));

        Assert.Null(_leitung.ChangePassword("Rettungshund2026", "Suchhund-Neu-2026", "Suchhund-Neu-2026"));
        Assert.False(_tokens.TryPruefen(token, jetzt, out _));
    }

    [Fact]
    public async Task Position_ErscheintMitGeraetename_NurWaehrendDesEinsatzes()
    {
        var einsatz = await LaufenderEinsatzAsync();
        var positionen = new ElPositionService(einsatz);
        ElPosition? gemeldet = null;
        positionen.Geaendert += p => gemeldet = p;
        var token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(24));
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeMilliseconds();

        var antwort = Controller(positionen, token).PostPositions(Punkte((49.31, 8.43, t0 + 30_000), (49.30, 8.42, t0)));

        Assert.Equal("track", Modus(antwort));
        Assert.Equal("Tablet EL", gemeldet!.Geraet);
        Assert.Equal(49.31, gemeldet.Latitude); // neueste Position
        Assert.Equal(64, gemeldet.BatteryPercent);
        Assert.Single(positionen.Aktuell);

        await einsatz.EndEinsatzAsync();
        Assert.Empty(positionen.Aktuell);
        Assert.Equal("pause", Modus(Controller(positionen, token).GetStatus())); // angemeldet bleiben, GPS aus
    }

    [Fact]
    public async Task OeffentlicherPort_UndOhneToken_KeinZugang()
    {
        var positionen = new ElPositionService(await LaufenderEinsatzAsync());
        var token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(24));

        Assert.IsType<NotFoundResult>(Controller(positionen, token, OeffentlicherPort).GetStatus());
        Assert.False(PublicAccess.IsPublicPath(new PathString("/api/el/app/v1/positions")));

        var ohne = Controller(positionen, null).PostPositions(Punkte((49.3, 8.4, 0)));
        Assert.Equal(StatusCodes.Status410Gone, ((ObjectResult)ohne).StatusCode);
        Assert.Empty(positionen.Aktuell);
    }
}

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

/// <summary>EL-Zugang im Einsatz verlaengern, hoechstens 72 h ab Anmeldung mit Passwort (#141)</summary>
public sealed class ElAppVerlaengernTests : IDisposable
{
    private const string Passwort = "Rettungshund2026";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-elverl-" + Guid.NewGuid().ToString("N"));
    private readonly LeitungAuthStore _leitung;
    private readonly EinsatzService _einsatz = new();
    private readonly ElAppTokenService _tokens;

    public ElAppVerlaengernTests()
    {
        Directory.CreateDirectory(_dir);
        _leitung = new LeitungAuthStore(_dir);
        Assert.Null(_leitung.Setup(_leitung.SetupCode!, Passwort, Passwort));
        _tokens = new ElAppTokenService(RandomNumberGenerator.GetBytes(64), _leitung);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ElAppController Controller(string token, int port = 5000)
    {
        var c = new ElAppController(_tokens, new ElPositionService(_einsatz), new LeitungsGeraetNamen(new VpnDeviceStore(_dir)),
            new PublicAccess(5002), NullLogger<ElAppController>.Instance, null, _leitung, new LoginLockout(), null, _einsatz);
        var http = new DefaultHttpContext();
        http.Connection.LocalPort = port;
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.9");
        http.Request.Headers.Authorization = "Bearer " + token;
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    private Task StartAsync() =>
        _einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 31, 6, 0, 0) });

    [Fact]
    public async Task ImEinsatz_NeuesToken_AltesGesperrt_AnmeldungWandertMit()
    {
        await StartAsync();
        var anmeldung = DateTime.UtcNow.AddHours(-20);
        var alt = _tokens.Ausstellen(DateTime.UtcNow.AddHours(4), anmeldung);

        var antwort = Json(await Controller(alt).Verlaengern(null));
        var neu = antwort.GetProperty("token").GetString()!;

        Assert.StartsWith("e3.", neu);
        Assert.True(_tokens.TryPruefen(neu, DateTime.UtcNow, out var ablauf, out var anmeldungNeu));
        Assert.True(ablauf > DateTime.UtcNow.AddHours(23));
        Assert.Equal(anmeldung.AddTicks(-(anmeldung.Ticks % TimeSpan.TicksPerSecond)), anmeldungNeu);
        Assert.EndsWith("Z", antwort.GetProperty("verlaengerbarBis").GetString());
        Assert.False(_tokens.TryPruefen(alt, DateTime.UtcNow, out _));
        Assert.Equal(410, ((ObjectResult)await Controller(alt).Verlaengern(null)).StatusCode);
    }

    [Fact]
    public async Task Hoechstens72Stunden_DannNeuAnmelden409()
    {
        await StartAsync();
        var anmeldung = DateTime.UtcNow.AddHours(-60);
        var token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(2), anmeldung);

        // Erst bis zur Grenze (72 h ab Anmeldung = in 12 h), dann nicht mehr
        var neu = Json(await Controller(token).Verlaengern(null)).GetProperty("token").GetString()!;
        Assert.True(_tokens.TryPruefen(neu, DateTime.UtcNow, out var ablauf));
        Assert.InRange(ablauf, DateTime.UtcNow.AddHours(11.9), DateTime.UtcNow.AddHours(12.1));

        Assert.IsType<ConflictObjectResult>(await Controller(neu).Verlaengern(null));
        Assert.True(_tokens.TryPruefen(neu, DateTime.UtcNow, out _)); // abgelehnt: bleibt gueltig
    }

    [Fact]
    public async Task OhneEinsatz409_NachPasswortaenderung410_Oeffentlich404()
    {
        var token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(4));
        Assert.IsType<ConflictObjectResult>(await Controller(token).Verlaengern(null));

        await StartAsync();
        Assert.IsType<NotFoundResult>(await Controller(token, port: 5002).Verlaengern(null));
        Assert.Null(_leitung.ChangePassword(Passwort, "NeuesPasswort2026", "NeuesPasswort2026"));
        Assert.Equal(410, ((ObjectResult)await Controller(token).Verlaengern(null)).StatusCode);
    }

    [Fact]
    public void AltesE2Token_AnmeldungGiltAlsAblaufMinusLaufzeit()
    {
        var geheim = RandomNumberGenerator.GetBytes(64);
        var tokens = new ElAppTokenService(geheim, _leitung);
        var ablauf = DateTimeOffset.UtcNow.AddHours(5).ToUnixTimeSeconds();
        var payload = $"e2.{_leitung.Generation}.{ablauf}.abc";
        using var hmac = new HMACSHA256(geheim);
        var signatur = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes("el|" + payload)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        Assert.True(tokens.TryPruefen($"{payload}.{signatur}", DateTime.UtcNow, out var ablaufUtc, out var anmeldung));
        Assert.Equal(ablaufUtc - ElAppTokenService.Laufzeit, anmeldung);
    }
}

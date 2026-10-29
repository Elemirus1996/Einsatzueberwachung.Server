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

/// <summary>Native Einsatzleitung, Stufe 2: Anmeldung, Abmeldung, Lage (#125)</summary>
public sealed class ElAppNativTests : IDisposable
{
    private const string Passwort = "Rettungshund2026";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-elnativ-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(64);
    private readonly LeitungAuthStore _leitung;
    private readonly LoginLockout _sperre = new();
    private readonly EinsatzService _einsatz = new();

    public ElAppNativTests()
    {
        Directory.CreateDirectory(_dir);
        _leitung = new LeitungAuthStore(_dir);
        Assert.Null(_leitung.Setup(_leitung.SetupCode!, Passwort, Passwort));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ElAppTokenService Tokens() => new(_secret, _leitung, Path.Combine(_dir, "el-app-abgemeldet.txt"));

    private ElAppController Controller(ElAppTokenService tokens, string? token = null, int port = 5000)
    {
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) =>
            m.Name == nameof(ISettingsService.GetStaffelSettingsAsync) ? Task.FromResult(new StaffelSettings { StaffelName = "RHS Test" }) : null);
        var audit = EinsatzAbschlussTests.Recorder<IAuditLogService>.Create((_, _) => null);
        var c = new ElAppController(tokens, new ElPositionService(_einsatz), new LeitungsGeraetNamen(new VpnDeviceStore(_dir)),
            new PublicAccess(5002), NullLogger<ElAppController>.Instance, null,
            _leitung, _sperre, audit.Proxy, _einsatz, new CollarTrackingService(_einsatz), settings.Proxy, new ServerKennung(_dir));
        var http = new DefaultHttpContext();
        http.Connection.LocalPort = port;
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.9");
        if (token is not null) http.Request.Headers.Authorization = "Bearer " + token;
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    [Fact]
    public async Task Login_LiefertTokenUndServer_FalschesPasswort401_SperreGemeinsamMitBrowser()
    {
        var tokens = Tokens();
        var ok = Json(await Controller(tokens).Login(new(Passwort, "iPhone Anna")));
        Assert.Equal("el", ok.GetProperty("rolle").GetString());
        Assert.True(tokens.TryPruefen(ok.GetProperty("token").GetString(), DateTime.UtcNow, out _));
        Assert.Equal("RHS Test", ok.GetProperty("server").GetProperty("staffel").GetString());
        Assert.Equal(new ServerKennung(_dir).Kennung, ok.GetProperty("server").GetProperty("kennung").GetString());

        Assert.IsType<UnauthorizedObjectResult>(await Controller(tokens).Login(new("falsch", null)));
        for (var i = 0; i < LoginLockout.MaxFailures; i++) _sperre.Fail("10.10.0.9", DateTime.UtcNow); // z. B. im Browser
        Assert.Equal(429, ((ObjectResult)await Controller(tokens).Login(new(Passwort, null))).StatusCode);
    }

    [Fact]
    public async Task Login_AmOeffentlichenZugang404()
    {
        Assert.IsType<NotFoundResult>(await Controller(Tokens(), port: 5002).Login(new(Passwort, null)));
        Assert.IsType<NotFoundResult>(Controller(Tokens(), port: 5002).GetLage());
    }

    [Fact]
    public async Task Logout_MachtTokenDauerhaftUngueltig_AuchNachNeustart()
    {
        var tokens = Tokens();
        var token = Json(await Controller(tokens).Login(new(Passwort, null))).GetProperty("token").GetString()!;

        Assert.IsType<NoContentResult>(Controller(tokens, token).Logout());
        Assert.False(tokens.TryPruefen(token, DateTime.UtcNow, out _));
        Assert.False(Tokens().TryPruefen(token, DateTime.UtcNow, out _)); // neuer Dienst = Neustart
        Assert.Equal(410, ((ObjectResult)Controller(tokens, token).GetLage()).StatusCode);
    }

    [Fact]
    public async Task Lage_OhneEinsatzPause_MitEinsatzTeamsUndWarnstufe()
    {
        var tokens = Tokens();
        var token = tokens.Ausstellen(DateTime.UtcNow.AddHours(1));

        var leer = Json(Controller(tokens, token).GetLage());
        Assert.False(leer.GetProperty("einsatz").GetProperty("aktiv").GetBoolean());
        Assert.Equal("pause", leer.GetProperty("mode").GetString());
        Assert.Equal(0, leer.GetProperty("teams").GetArrayLength());

        await _einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", Stichwort = "VP", IstEinsatz = false, EinsatzDatum = new DateTime(2026, 10, 29, 9, 0, 0) });
        var team = await _einsatz.AddTeamAsync(new Team { TeamName = "Team Rex", FirstWarningMinutes = 1, SecondWarningMinutes = 2 });
        team.StartTimer(DateTime.Now.AddMinutes(-90)); // laeuft seit 90 min (Ortszeit wie ITimeService)

        var lage = Json(Controller(tokens, token).GetLage());
        Assert.True(lage.GetProperty("einsatz").GetProperty("aktiv").GetBoolean());
        Assert.True(lage.GetProperty("einsatz").GetProperty("uebung").GetBoolean());
        Assert.Equal("track", lage.GetProperty("mode").GetString());
        var t = Assert.Single(lage.GetProperty("teams").EnumerateArray());
        Assert.Equal("laeuft", t.GetProperty("suche").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, t.GetProperty("halsband").ValueKind);
        Assert.EndsWith("Z", t.GetProperty("suche").GetProperty("seit").GetString());
    }
}

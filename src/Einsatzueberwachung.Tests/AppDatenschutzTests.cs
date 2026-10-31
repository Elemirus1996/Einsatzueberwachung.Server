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

/// <summary>Datenschutzerklaerung der Staffel vom Server (#148)</summary>
public sealed class AppDatenschutzTests : IDisposable
{
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(64);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-ds-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _einstellungen = new();
    private readonly EinsatzService _einsatz = new();

    public AppDatenschutzTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ISettingsService Settings() => EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) => m.Name switch
    {
        nameof(ISettingsService.GetAppSettingsAsync) => Task.FromResult(_einstellungen),
        nameof(ISettingsService.GetStaffelSettingsAsync) => Task.FromResult(new StaffelSettings { StaffelName = "RHS Test" }),
        _ => null
    }).Proxy;

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    private void Hinterlegen(string text)
    {
        _einstellungen.AppDatenschutzTitel = "Datenschutz RHS Test";
        _einstellungen.AppDatenschutzText = text;
        AppDatenschutz.Aktualisieren(_einstellungen, DateTime.UtcNow);
    }

    [Fact]
    public void Speichern_SetztVersionUndStand_NurBeiAenderung()
    {
        // SettingsService.SaveAppSettingsAsync ruft genau das vor dem Schreiben auf
        var s = new AppSettings { AppDatenschutzText = "## 1. Verantwortlich\nRHS Test" };
        AppDatenschutz.Aktualisieren(s, new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc));
        var version = s.AppDatenschutzVersion;
        Assert.NotNull(version);
        Assert.Equal(new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc), s.AppDatenschutzStandUtc);

        s.AppUploadSekunden = 90; // andere Einstellung: Fassung bleibt
        AppDatenschutz.Aktualisieren(s, new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(version, s.AppDatenschutzVersion);
        Assert.Equal(9, s.AppDatenschutzStandUtc!.Value.Hour);

        s.AppDatenschutzText += "\nNeu";
        AppDatenschutz.Aktualisieren(s, new DateTime(2026, 10, 31, 11, 0, 0, DateTimeKind.Utc));
        Assert.NotEqual(version, s.AppDatenschutzVersion);
        Assert.Equal(11, s.AppDatenschutzStandUtc!.Value.Hour);

        s.AppDatenschutzText = "  ";
        AppDatenschutz.Aktualisieren(s, DateTime.UtcNow);
        Assert.Null(s.AppDatenschutzVersion);
        Assert.Null(s.AppDatenschutzStandUtc);
    }

    [Fact]
    public async Task Team_LoginTeamsGanz_ConfigUndLageNurVersion_NachlesenOderNull()
    {
        var tokens = new TeamMobileTokenService(Secret, _einsatz);
        await _einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 31, 9, 0, 0) });
        var team = await _einsatz.AddTeamAsync(new Team { TeamName = "Team Rex" });
        TeamMobileAppController Controller(string? token = null)
        {
            var c = new TeamMobileAppController(tokens, _einsatz, Settings(), NullLogger<TeamMobileAppController>.Instance,
                new CollarTrackingService(_einsatz));
            var http = new DefaultHttpContext();
            if (token is not null) http.Request.Headers.Authorization = "Bearer " + token;
            c.ControllerContext = new ControllerContext { HttpContext = http };
            return c;
        }
        var token = tokens.IssueAppToken(team.TeamId, DateTime.UtcNow.AddHours(24));

        // Nichts hinterlegt: null bzw. 404
        var ohne = Json(await Controller().LoginTeams(new() { Code = tokens.CurrentMasterToken }));
        Assert.Equal(JsonValueKind.Null, ohne.GetProperty("datenschutz").ValueKind);
        Assert.IsType<NotFoundResult>(await Controller(token).GetDatenschutz());

        Hinterlegen("## 1. Verantwortlich\nRHS Test");
        var ds = Json(await Controller().LoginTeams(new() { Code = tokens.CurrentMasterToken })).GetProperty("datenschutz");
        Assert.Equal("Datenschutz RHS Test", ds.GetProperty("titel").GetString());
        Assert.StartsWith("## 1.", ds.GetProperty("text").GetString());
        Assert.EndsWith("Z", ds.GetProperty("stand").GetString());
        var version = ds.GetProperty("version").GetString();

        var config = Json(await Controller(token).GetConfig()).GetProperty("datenschutz");
        Assert.Equal(version, config.GetProperty("version").GetString());
        Assert.False(config.TryGetProperty("text", out _));
        Assert.Equal(version, Json(await Controller(token).GetLage()).GetProperty("datenschutz").GetProperty("version").GetString());
        Assert.Equal(version, Json(await Controller(token).GetDatenschutz()).GetProperty("version").GetString());
        Assert.Equal(410, ((ObjectResult)await Controller("falsch").GetDatenschutz()).StatusCode);
    }

    [Fact]
    public async Task Einsatzleitung_LoginGanz_LageNurVersion_NachlesenNurIntern()
    {
        var leitung = new LeitungAuthStore(_dir);
        Assert.Null(leitung.Setup(leitung.SetupCode!, "Rettungshund2026", "Rettungshund2026"));
        var tokens = new ElAppTokenService(Secret, leitung);
        ElAppController Controller(string? token = null, int port = 5000)
        {
            var c = new ElAppController(tokens, new ElPositionService(_einsatz), new LeitungsGeraetNamen(new VpnDeviceStore(_dir)),
                new PublicAccess(5002), NullLogger<ElAppController>.Instance, null, leitung, new LoginLockout(), null, _einsatz,
                null, Settings());
            var http = new DefaultHttpContext();
            http.Connection.LocalPort = port;
            http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.9");
            if (token is not null) http.Request.Headers.Authorization = "Bearer " + token;
            c.ControllerContext = new ControllerContext { HttpContext = http };
            return c;
        }
        Hinterlegen("Text der Staffel");

        var login = Json(await Controller().Login(new("Rettungshund2026", null)));
        var version = login.GetProperty("datenschutz").GetProperty("version").GetString();
        Assert.Equal("Text der Staffel", login.GetProperty("datenschutz").GetProperty("text").GetString());
        var token = login.GetProperty("token").GetString()!;

        Assert.Equal(version, Json(await Controller(token).GetLage()).GetProperty("datenschutz").GetProperty("version").GetString());
        Assert.Equal(version, Json(await Controller(token).GetDatenschutz()).GetProperty("version").GetString());
        Assert.IsType<NotFoundResult>(await Controller(token, port: 5002).GetDatenschutz());
    }

    [Fact]
    public void Vorlage_HatPlatzhalterUndKeineHtmlTags()
    {
        Assert.Contains("[Name der Staffel", AppDatenschutzAntwort.Vorlage);
        Assert.Contains("Frist, in den Einstellungen des Servers festgelegt", AppDatenschutzAntwort.Vorlage);
        Assert.DoesNotContain("<", AppDatenschutzAntwort.Vorlage);
        Assert.True(AppDatenschutzAntwort.Vorlage.Length < AppDatenschutz.MaxTextLaenge);
    }
}

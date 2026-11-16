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

/// <summary>Packliste der Staffel fuer die App (APP_API Team-Rolle Abschnitt 14)</summary>
public sealed class AppPacklisteTests : IDisposable
{
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(64);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-pl-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _einstellungen = new();
    private readonly EinsatzService _einsatz = new();

    public AppPacklisteTests() => Directory.CreateDirectory(_dir);

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
        _einstellungen.AppPacklisteText = text;
        AppPackliste.Aktualisieren(_einstellungen, DateTime.UtcNow);
    }

    [Fact]
    public void Punkte_GruppenListenzeichenLeerzeilen()
    {
        var punkte = AppPackliste.Punkte("Stirnlampe\r\n\r\nPersönlich:\n- Regenjacke\n* Handschuhe\nHund:\n  Wasser und Napf  \n:\nOhne Gruppe");
        Assert.Equal(new[] { "Stirnlampe", "Regenjacke", "Handschuhe", "Wasser und Napf", "Ohne Gruppe" }, punkte.Select(p => p.Text));
        Assert.Equal(new string?[] { null, "Persönlich", "Persönlich", "Hund", null }, punkte.Select(p => p.Gruppe));
        Assert.All(punkte, p => Assert.Equal(8, p.Id.Length));
        Assert.Empty(AppPackliste.Punkte("  \n Gruppe:\n"));
    }

    [Fact]
    public void Id_StabilJePunkt_DoppelteEindeutig()
    {
        var vorher = AppPackliste.Punkte("Hund:\nNapf\nLeine");
        var nachher = AppPackliste.Punkte("Hund:\nLeine\nNapf\nKenndecke");
        Assert.Equal(vorher.Single(p => p.Text == "Napf").Id, nachher.Single(p => p.Text == "Napf").Id);
        // gleicher Text in anderer Gruppe ist ein anderer Punkt
        Assert.NotEqual(AppPackliste.Punkte("A:\nNapf")[0].Id, AppPackliste.Punkte("B:\nNapf")[0].Id);
        var doppelt = AppPackliste.Punkte("Napf\nNapf");
        Assert.Equal(2, doppelt.Select(p => p.Id).Distinct().Count());
        Assert.EndsWith("-2", doppelt[1].Id);
    }

    [Fact]
    public void Grenzen_TextGruppeAnzahl()
    {
        var punkte = AppPackliste.Punkte(new string('G', 100) + ":\n" + new string('x', 500));
        Assert.Equal(AppPackliste.MaxTextLaenge, punkte[0].Text.Length);
        Assert.Equal(AppPackliste.MaxGruppeLaenge, punkte[0].Gruppe!.Length);
        Assert.Equal(AppPackliste.MaxPunkte, AppPackliste.Punkte(string.Join("\n", Enumerable.Range(1, 300).Select(i => "Punkt " + i))).Count);
    }

    [Fact]
    public void Speichern_SetztVersionUndStand_NurBeiAenderung()
    {
        var s = new AppSettings { AppPacklisteText = "Stirnlampe" };
        AppPackliste.Aktualisieren(s, new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc));
        var version = s.AppPacklisteVersion;
        Assert.Equal(16, version!.Length);

        s.AppPacklisteText = "  Stirnlampe  \n\n"; // gleiche Punkte: Fassung bleibt
        AppPackliste.Aktualisieren(s, new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(version, s.AppPacklisteVersion);
        Assert.Equal(9, s.AppPacklisteStandUtc!.Value.Hour);

        s.AppPacklisteText += "\nRegenjacke";
        AppPackliste.Aktualisieren(s, new DateTime(2026, 11, 16, 11, 0, 0, DateTimeKind.Utc));
        Assert.NotEqual(version, s.AppPacklisteVersion);
        Assert.Equal(11, s.AppPacklisteStandUtc!.Value.Hour);

        s.AppPacklisteText = "";
        AppPackliste.Aktualisieren(s, DateTime.UtcNow);
        Assert.Null(s.AppPacklisteVersion);
        Assert.Null(s.AppPacklisteStandUtc);
    }

    [Fact]
    public async Task Team_VersionInServer_PacklisteMitToken()
    {
        var tokens = new TeamMobileTokenService(Secret, _einsatz);
        await _einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 11, 16, 9, 0, 0) });
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

        var ohne = Json(await Controller().LoginTeams(new() { Code = tokens.CurrentMasterToken }));
        Assert.Equal(JsonValueKind.Null, ohne.GetProperty("server").GetProperty("packlisteVersion").ValueKind);
        Assert.IsType<NotFoundResult>(await Controller(token).GetPackliste());

        Hinterlegen("Persönlich:\nStirnlampe\nHund:\nWasser und Napf");
        var version = Json(await Controller().LoginTeams(new() { Code = tokens.CurrentMasterToken })).GetProperty("server").GetProperty("packlisteVersion").GetString();
        Assert.Equal(version, Json(await Controller(token).GetConfig()).GetProperty("server").GetProperty("packlisteVersion").GetString());

        var liste = Json(await Controller(token).GetPackliste());
        Assert.Equal(version, liste.GetProperty("version").GetString());
        Assert.EndsWith("Z", liste.GetProperty("stand").GetString());
        var punkte = liste.GetProperty("punkte").EnumerateArray().ToList();
        Assert.Equal(2, punkte.Count);
        Assert.Equal("Stirnlampe", punkte[0].GetProperty("text").GetString());
        Assert.Equal("Hund", punkte[1].GetProperty("gruppe").GetString());
        Assert.Equal(8, punkte[0].GetProperty("id").GetString()!.Length);
        Assert.Equal(410, ((ObjectResult)await Controller("falsch").GetPackliste()).StatusCode);
    }

    [Fact]
    public async Task Einsatzleitung_VersionBeimLogin_PacklisteNurIntern()
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
        Hinterlegen("Stirnlampe");

        var login = Json(await Controller().Login(new("Rettungshund2026", null)));
        var version = login.GetProperty("server").GetProperty("packlisteVersion").GetString();
        Assert.NotNull(version);
        var token = login.GetProperty("token").GetString()!;

        Assert.Equal(version, Json(await Controller(token).GetPackliste()).GetProperty("version").GetString());
        Assert.IsType<NotFoundResult>(await Controller(token, port: 5002).GetPackliste());
        Assert.Equal(410, ((ObjectResult)await Controller("falsch").GetPackliste()).StatusCode);
    }
}

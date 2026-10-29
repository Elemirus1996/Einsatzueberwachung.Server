using System.Security.Cryptography;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Einsatzueberwachung.Tests;

/// <summary>Heimatstaffel in der App: Server-Kennung und Logo (#121, docs/APP_API.md Abschnitt 10)</summary>
public sealed class HeimatstaffelTests : IDisposable
{
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(64);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-heimat-" + Guid.NewGuid().ToString("N"));
    private readonly StaffelSettings _staffel = new() { StaffelName = "RHS Test" };

    public HeimatstaffelTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ISettingsService Settings() => EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) => m.Name switch
    {
        nameof(ISettingsService.GetAppSettingsAsync) => Task.FromResult(new AppSettings()),
        nameof(ISettingsService.GetStaffelSettingsAsync) => Task.FromResult(_staffel),
        _ => null
    }).Proxy;

    private string Bild(string name, int breite, int hoehe, SKColor farbe, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(breite, hoehe);
        bitmap.Erase(farbe);
        using var bild = SKImage.FromBitmap(bitmap);
        using var daten = bild.Encode(format, 90);
        var pfad = Path.Combine(_dir, name);
        File.WriteAllBytes(pfad, daten.ToArray());
        return pfad;
    }

    [Fact]
    public void Kennung_BleibtUeberNeustartsGleich()
    {
        var erste = new ServerKennung(_dir).Kennung;
        Assert.True(Guid.TryParse(erste, out _));
        Assert.Equal(erste, new ServerKennung(_dir).Kennung);

        File.WriteAllText(Path.Combine(_dir, ServerKennung.DateiName), "kaputt");
        Assert.NotEqual(erste, new ServerKennung(_dir).Kennung); // unlesbar → neu erzeugt, nicht abstuerzen
    }

    [Fact]
    public async Task Logo_WirdVerkleinertUndVersionAendertSichMitNeuemLogo()
    {
        _staffel.StaffelLogoPfad = Bild("logo.jpg", 1200, 600, SKColors.Red, SKEncodedImageFormat.Jpeg);
        var appLogo = new AppLogo(Settings());

        var logo = (await appLogo.HolenAsync())!;
        using var png = SKBitmap.Decode(logo.Png);
        Assert.Equal((512, 256), (png.Width, png.Height));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, logo.Png[..4]); // PNG
        Assert.Equal(16, logo.Version.Length);

        _staffel.StaffelLogoPfad = Bild("logo2.png", 100, 100, SKColors.Blue);
        var neu = (await appLogo.HolenAsync())!;
        Assert.NotEqual(logo.Version, neu.Version);
        using var klein = SKBitmap.Decode(neu.Png);
        Assert.Equal(100, klein.Width); // kleine Logos werden nicht vergroessert
    }

    [Theory]
    [InlineData("")]
    [InlineData("fehlt.png")]
    [InlineData("kaputt.png")]
    public async Task KeinLogoFuerDieApp(string datei)
    {
        File.WriteAllText(Path.Combine(_dir, "kaputt.png"), "kein bild");
        _staffel.StaffelLogoPfad = datei == "" ? "" : Path.Combine(_dir, datei);

        Assert.Null(await new AppLogo(Settings()).HolenAsync());
    }

    [Fact]
    public async Task SvgLogo_WirdGerastert()
    {
        var pfad = Path.Combine(_dir, "logo.svg");
        File.WriteAllText(pfad, "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 100'><rect width='200' height='100' fill='#c00'/></svg>");
        _staffel.StaffelLogoPfad = pfad;

        var logo = (await new AppLogo(Settings()).HolenAsync())!;

        using var png = SKBitmap.Decode(logo.Png);
        Assert.Equal((512, 256), (png.Width, png.Height));
        Assert.Equal(new SKColor(0xcc, 0, 0), png.GetPixel(256, 128)); // gezeichnet, nicht leer
    }

    [Fact]
    public void SvgBereinigung_EntferntSkripteUndExterneVerweise()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" onload="alert(1)">
              <script>alert(2)</script>
              <style>@import url(https://boese.example/x.css);</style>
              <foreignObject><div>x</div></foreignObject>
              <image href="https://boese.example/pixel.png" width="1" height="1"/>
              <image xlink:href="file:///etc/passwd" width="1" height="1"/>
              <use href="#form"/>
              <image href="data:image/png;base64,iVBORw0KGgo=" width="1" height="1"/>
              <rect id="form" width="10" height="10" style="fill:url(https://boese.example/f)" onclick="x()"/>
            </svg>
            """;

        var sauber = AppLogo.SvgBereinigen(System.Text.Encoding.UTF8.GetBytes(svg))!;

        Assert.DoesNotContain("script", sauber);
        Assert.DoesNotContain("onload", sauber);
        Assert.DoesNotContain("onclick", sauber);
        Assert.DoesNotContain("@import", sauber);
        Assert.DoesNotContain("foreignObject", sauber);
        Assert.DoesNotContain("boese.example", sauber);
        Assert.DoesNotContain("file:", sauber);
        Assert.Contains("href=\"#form\"", sauber);                 // interner Verweis bleibt
        Assert.Contains("data:image/png;base64", sauber);           // eingebettetes Bild bleibt
    }

    [Theory]
    [InlineData("<!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><svg xmlns='http://www.w3.org/2000/svg'>&x;</svg>")]
    [InlineData("<html><body>kein svg</body></html>")]
    [InlineData("kein xml")]
    public void SvgBereinigung_LehntDtdUndFremdesAb(string inhalt)
    {
        Assert.Null(AppLogo.SvgBereinigen(System.Text.Encoding.UTF8.GetBytes(inhalt)));
        Assert.Null(AppLogo.SvgAlsPng(System.Text.Encoding.UTF8.GetBytes(inhalt)));
    }

    private async Task<(TeamMobileAppController Controller, string Token, EinsatzService Einsatz)> TeamAsync(string? token = null)
    {
        var einsatz = new EinsatzService();
        var tokens = new TeamMobileTokenService(Secret, einsatz);
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 28, 9, 0, 0) });
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Team Rex" });
        var halsbaender = new CollarTrackingService(einsatz);
        var appToken = tokens.IssueAppToken(team.TeamId, DateTime.UtcNow.AddHours(1));
        var controller = new TeamMobileAppController(tokens, einsatz, Settings(), NullLogger<TeamMobileAppController>.Instance,
            halsbaender, new TeamAppEreignisse(einsatz, halsbaender, tokens), new AppStatusmeldungen(), new ServerKennung(_dir), new AppLogo(Settings()));
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer " + (token ?? appToken);
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, appToken, einsatz);
    }

    [Fact]
    public async Task Config_EnthaeltServerKennungUndLogoStand()
    {
        _staffel.StaffelLogoPfad = Bild("logo.png", 64, 64, SKColors.Green);
        var (controller, _, _) = await TeamAsync();

        var json = JsonSerializer.SerializeToElement(((ObjectResult)await controller.GetConfig()).Value);
        var server = json.GetProperty("server");

        Assert.Equal("RHS Test", json.GetProperty("staffel").GetString()); // bisheriges Feld bleibt
        Assert.Equal(new ServerKennung(_dir).Kennung, server.GetProperty("kennung").GetString());
        Assert.Equal("RHS Test", server.GetProperty("staffel").GetString());
        Assert.Equal((await new AppLogo(Settings()).HolenAsync())!.Version, server.GetProperty("logoVersion").GetString());
    }

    [Fact]
    public async Task LogoEndpunkt_PngEtag304_404OhneLogo_410OhneToken()
    {
        _staffel.StaffelLogoPfad = Bild("logo.png", 64, 64, SKColors.Green);
        var (controller, token, _) = await TeamAsync();

        var datei = Assert.IsType<FileContentResult>(await controller.GetLogo());
        Assert.Equal("image/png", datei.ContentType);
        var etag = controller.Response.Headers.ETag.ToString();
        Assert.StartsWith("\"", etag);

        var (zweiter, _, _) = await TeamAsync();
        zweiter.Request.Headers.IfNoneMatch = etag;
        Assert.Equal(304, Assert.IsType<StatusCodeResult>(await zweiter.GetLogo()).StatusCode);

        _staffel.StaffelLogoPfad = "";
        var (ohneLogo, _, _) = await TeamAsync();
        Assert.IsType<NotFoundObjectResult>(await ohneLogo.GetLogo());

        var (falsch, _, _) = await TeamAsync("a1.falsch.1.1.x");
        Assert.Equal(410, ((ObjectResult)await falsch.GetLogo()).StatusCode);
    }
}

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

/// <summary>Mitteilungen fuer die Einsatzleitung im Hintergrund ohne Push (#79)</summary>
public sealed class ElMitteilungenTests : IDisposable
{
    private const string Passwort = "Rettungshund2026";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-elmitt-" + Guid.NewGuid().ToString("N"));
    private readonly LeitungAuthStore _leitung;
    private readonly EinsatzService _einsatz = new();
    private readonly WarningService _warnungen;
    private readonly ElMitteilungen _mitteilungen;
    private readonly ElAppTokenService _tokens;

    public ElMitteilungenTests()
    {
        Directory.CreateDirectory(_dir);
        _leitung = new LeitungAuthStore(_dir);
        Assert.Null(_leitung.Setup(_leitung.SetupCode!, Passwort, Passwort));
        _warnungen = new WarningService(EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) =>
            m.Name == nameof(ISettingsService.GetAppSettingsAsync) ? Task.FromResult(new AppSettings()) : null).Proxy);
        _mitteilungen = new ElMitteilungen(_einsatz, _warnungen);
        _tokens = new ElAppTokenService(RandomNumberGenerator.GetBytes(64), _leitung, Path.Combine(_dir, "abgemeldet.txt"));
    }

    public void Dispose()
    {
        _mitteilungen.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private ElAppController Controller(string? token, int port = 5000, bool mitMitteilungen = true)
    {
        var c = new ElAppController(_tokens, new ElPositionService(_einsatz), new LeitungsGeraetNamen(new VpnDeviceStore(_dir)),
            new PublicAccess(5002), NullLogger<ElAppController>.Instance, null,
            _leitung, new LoginLockout(), null, _einsatz, mitteilungen: mitMitteilungen ? _mitteilungen : null);
        var http = new DefaultHttpContext();
        http.Connection.LocalPort = port;
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.9");
        if (token is not null) http.Request.Headers.Authorization = "Bearer " + token;
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    private async Task<Team> EinsatzMitTeam()
    {
        await _einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 11, 18, 9, 0, 0) });
        return await _einsatz.AddTeamAsync(new Team { TeamName = "Team Rex", FirstWarningMinutes = 45, SecondWarningMinutes = 60 });
    }

    [Fact]
    public async Task Status_TraegtStand_DerSichBeiNeuerMitteilungAendert()
    {
        var token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(1));
        var team = await EinsatzMitTeam();

        var vorher = Json(await Controller(token).GetStatus());
        Assert.Equal(30, vorher.GetProperty("uploadEverySec").GetInt32());
        var stand = vorher.GetProperty("mitteilungStand").GetString();

        _warnungen.AddWarning(new WarningEntry { Source = WarningRuleDefinition.Sources.CollarOutOfBounds, TeamId = team.TeamId, Level = WarningLevel.Critical,
            Message = "Halsband \"HB-7\" (Team: Team Rex) hat das zugewiesene Suchgebiet verlassen." });

        var nachher = Json(await Controller(token).GetStatus()).GetProperty("mitteilungStand").GetString();
        Assert.NotEqual(stand, nachher);

        var antwort = Json(Controller(token).GetMitteilungen(stand));
        Assert.Equal(nachher, antwort.GetProperty("stand").GetString());
        var m = Assert.Single(antwort.GetProperty("mitteilungen").EnumerateArray().ToList());
        Assert.Equal("gebiet", m.GetProperty("art").GetString());
        Assert.Equal(2, m.GetProperty("stufe").GetInt32());
        Assert.Equal("Team Rex: Hund hat das Suchgebiet verlassen", m.GetProperty("text").GetString());

        // Nichts Neues seit dem letzten Stand
        Assert.Empty(Json(Controller(token).GetMitteilungen(nachher)).GetProperty("mitteilungen").EnumerateArray());
    }

    [Fact]
    public async Task UnbekannterStand_LeereListe_KeineFlut()
    {
        var token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(1));
        var team = await EinsatzMitTeam();
        _warnungen.AddWarning(new WarningEntry { Source = WarningRuleDefinition.Sources.PhoneNoSignal, TeamId = team.TeamId });

        foreach (var seit in new string?[] { null, "", "fremd-3", "abc" })
        {
            var a = Json(Controller(token).GetMitteilungen(seit));
            Assert.Empty(a.GetProperty("mitteilungen").EnumerateArray());
            Assert.Equal(_mitteilungen.Stand, a.GetProperty("stand").GetString());
        }
    }

    [Fact]
    public async Task Arten_UndTexte_OhneInhalt()
    {
        var team = await EinsatzMitTeam();
        var start = _mitteilungen.Stand;

        _warnungen.AddWarning(new WarningEntry { Source = WarningRuleDefinition.Sources.PhoneNoSignal, TeamId = team.TeamId });
        _warnungen.AddWarning(new WarningEntry { Source = WarningRuleDefinition.Sources.CollarNoSignal, TeamId = team.TeamId });
        _warnungen.AddWarning(new WarningEntry { Source = WarningRuleDefinition.Sources.CollarLowBattery, TeamId = team.TeamId });
        _warnungen.AddWarning(new WarningEntry { Source = WarningRuleDefinition.Sources.TeamAppPunkt, TeamId = team.TeamId, Level = WarningLevel.Warning,
            Message = "Team Rex meldet einen Fund: Person am Hochsitz Nord" });
        _warnungen.AddWarning(new WarningEntry { Source = WarningRuleDefinition.Sources.TeamAppPunkt, TeamId = team.TeamId, Level = WarningLevel.Info,
            Message = "Team Rex hat einen Punkt gesetzt: Spur" });
        _warnungen.AddWarning(new WarningEntry { Source = WarningRuleDefinition.Sources.ServerStorage });
        await _einsatz.AddGlobalNoteWithSourceAsync("Vermisste Erika gesehen an der Brücke", team.TeamId, team.TeamName, "Funk");
        await _einsatz.AddGlobalNoteWithSourceAsync("[Team Rex] im Suchgebiet eingetroffen", team.TeamId, team.TeamName, "Mobile",
            Einsatzueberwachung.Domain.Models.Enums.GlobalNotesEntryType.Manual, "Team-App");
        // Freitext der Team-Webseite: nur „neue Statusmeldung“
        await _einsatz.AddGlobalNoteWithSourceAsync("[Team Rex] Erika am Hochsitz gefunden", team.TeamId, team.TeamName, "Mobile",
            Einsatzueberwachung.Domain.Models.Enums.GlobalNotesEntryType.Manual, "Team Mobile");
        await _einsatz.AddGlobalNoteWithSourceAsync("Lagebesprechung 10 Uhr", "", "", "Einsatzleitung");

        var liste = _mitteilungen.Seit(start).Liste;
        Assert.Equal(new[] { "funkstille", "funkstille", "akku", "fund", "funk", "status", "status" }, liste.Select(m => m.Art));
        Assert.Equal(new[]
        {
            "Team Rex: Handy sendet nicht", "Team Rex: Halsband sendet nicht", "Team Rex: Halsband-Akku niedrig",
            "Team Rex meldet einen Fund", "Neuer Funkspruch von Team Rex", "Team Rex: im Suchgebiet eingetroffen", "Team Rex: neue Statusmeldung"
        }, liste.Select(m => m.Text));
        Assert.DoesNotContain(liste, m => m.Text.Contains("Erika") || m.Text.Contains("Hochsitz"));
    }

    [Fact]
    public async Task Einsatzende_UndNeuerEinsatzLeert()
    {
        var team = await EinsatzMitTeam();
        var start = _mitteilungen.Stand;
        _warnungen.AddWarning(new WarningEntry { Source = WarningRuleDefinition.Sources.PhoneNoSignal, TeamId = team.TeamId });

        await _einsatz.EndEinsatzAsync();
        var liste = _mitteilungen.Seit(start).Liste;
        Assert.Equal("einsatzende", liste[^1].Art);
        Assert.Null(liste[^1].TeamId);

        // Ohne Einsatz wird nichts gesammelt
        var ende = _mitteilungen.Stand;
        _warnungen.AddWarning(new WarningEntry { Source = WarningRuleDefinition.Sources.PhoneNoSignal, TeamId = team.TeamId });
        Assert.Equal(ende, _mitteilungen.Stand);

        await EinsatzMitTeam();
        Assert.Empty(_mitteilungen.Seit(start).Liste);
    }

    [Fact]
    public async Task Begrenzt_UndAmOeffentlichenZugang404()
    {
        var token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(1));
        var team = await EinsatzMitTeam();
        var start = _mitteilungen.Stand;
        for (var i = 0; i < ElMitteilungen.MaxGespeichert + 10; i++)
            _mitteilungen.Hinzufuegen("status", 1, team.TeamId, $"Team Rex: {i}");
        var liste = _mitteilungen.Seit(start).Liste;
        Assert.Equal(ElMitteilungen.MaxJeAntwort, liste.Count);
        Assert.Equal($"Team Rex: {ElMitteilungen.MaxGespeichert + 9}", liste[^1].Text);

        Assert.IsType<NotFoundResult>(Controller(token, port: 5002).GetMitteilungen(start));
        Assert.Equal(410, ((ObjectResult)Controller("falsch").GetMitteilungen(start)).StatusCode);
        // Ohne Dienst (aeltere Einrichtung): kein Stand, Takt wie bisher
        var ohne = Json(await Controller(token, mitMitteilungen: false).GetStatus());
        Assert.Equal(60, ohne.GetProperty("uploadEverySec").GetInt32());
    }
}

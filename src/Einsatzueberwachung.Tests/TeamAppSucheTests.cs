using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Team steuert die Suche selbst, Ortung in der Kurzpause (#133)</summary>
public class TeamAppSucheTests
{
    private static readonly byte[] Secret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(64);

    private sealed class Uhr : ITimeService
    {
        public DateTime Now => Ortszeit.Jetzt;
        public void Refresh() { }
    }

    private sealed record Umgebung(EinsatzService Einsatz, TeamMobileTokenService Tokens, CollarTrackingService Halsbaender,
        TeamAppEreignisse Ereignisse, TeamAppSuche Suche, EinsatzAbschlussTests.Recorder<IWarningService> Hinweise,
        EinsatzAbschlussTests.Recorder<IAuditLogService> Audit, Team Team, string Token)
    {
        public AppSettings Einstellungen { get; } = new();
    }

    private static async Task<Umgebung> AufbauAsync()
    {
        var einsatz = new EinsatzService();
        var tokens = new TeamMobileTokenService(Secret, einsatz);
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 29, 9, 0, 0) });
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Team Rex", DogName = "Rex" });
        var halsbaender = new CollarTrackingService(einsatz);
        var hinweise = EinsatzAbschlussTests.Recorder<IWarningService>.Create((_, _) => null);
        var audit = EinsatzAbschlussTests.Recorder<IAuditLogService>.Create((_, _) => null);
        var suche = new TeamAppSuche(einsatz, new TeamSucheService(einsatz, halsbaender, new Uhr()), hinweise.Proxy, audit.Proxy);
        return new Umgebung(einsatz, tokens, halsbaender, new TeamAppEreignisse(einsatz, halsbaender, tokens), suche, hinweise, audit,
            team, tokens.IssueAppToken(team.TeamId, DateTime.UtcNow.AddHours(24)));
    }

    private static TeamMobileAppController Controller(Umgebung u, string? token = null)
    {
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) => m.Name switch
        {
            nameof(ISettingsService.GetAppSettingsAsync) => Task.FromResult(u.Einstellungen),
            nameof(ISettingsService.GetStaffelSettingsAsync) => Task.FromResult(new StaffelSettings { StaffelName = "RHS Test" }),
            _ => null
        });
        var controller = new TeamMobileAppController(u.Tokens, u.Einsatz, settings.Proxy, NullLogger<TeamMobileAppController>.Instance,
            u.Halsbaender, u.Ereignisse, new AppStatusmeldungen(), suche: u.Suche);
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer " + (token ?? u.Token);
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static Task<IActionResult> Senden(Umgebung u, string aktion, string? id = null) =>
        Controller(u).PostSuche(new() { Id = id ?? Guid.NewGuid().ToString(), Aktion = aktion });

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    private static string Status(IActionResult r) => Json(r).GetProperty("suche").GetProperty("status").GetString()!;

    [Fact]
    public async Task Kurzpause_OrtungBleibtAn_PositionenNichtImLaufweg()
    {
        var u = await AufbauAsync();
        u.Team.StartTimer();
        await u.Einsatz.UpdateTeamPhoneLocationAsync(u.Team.TeamId, 49.31, 8.43, timestampUtc: DateTime.UtcNow.AddSeconds(-30));

        u.Team.PauseTimer();
        Assert.Equal(OrtungsModus.Track, OrtungsSteuerung.FuerTeam(u.Einsatz.CurrentEinsatz, u.Team));
        await u.Einsatz.UpdateTeamPhoneLocationAsync(u.Team.TeamId, 49.32, 8.44, timestampUtc: DateTime.UtcNow);

        Assert.Equal(49.32, u.Einsatz.PhoneLocations[u.Team.TeamId].Latitude); // aktuelle Position
        Assert.Single(u.Einsatz.GetPhoneTrackHistory(u.Team.TeamId));         // Laufweg ohne Kurzpause

        u.Team.StopTimer();
        Assert.Equal(OrtungsModus.Pause, OrtungsSteuerung.FuerTeam(u.Einsatz.CurrentEinsatz, u.Team));
    }

    [Fact]
    public async Task Ablauf_StartenPauseWeiterBeenden_MitModusHinweisUndAudit()
    {
        var u = await AufbauAsync();

        var start = await Senden(u, "starten");
        Assert.Equal("laeuft", Status(start));
        Assert.Equal("track", Json(start).GetProperty("mode").GetString());
        Assert.False(Json(start).GetProperty("doppelt").GetBoolean());

        Assert.Equal("kurzpause", Status(await Senden(u, "pause")));
        Assert.Equal("laeuft", Status(await Senden(u, "weiter")));
        var ende = await Senden(u, "beenden");
        Assert.Equal("beendet", Status(ende));
        Assert.Equal("pause", Json(ende).GetProperty("mode").GetString());

        Assert.Equal(4, u.Hinweise.Calls.Count(c => c == nameof(IWarningService.AddWarning)));
        Assert.Equal(4, u.Audit.Calls.Count(c => c == nameof(IAuditLogService.Log)));
        var notizen = u.Einsatz.GlobalNotes.Where(n => n.SourceType == TeamAppSuche.QuelleTeamApp).Select(n => n.Text).ToList();
        Assert.Contains("[Team Rex] hat die Suche gestartet (App)", notizen);
        Assert.Contains("[Team Rex] hat die Suche beendet (App)", notizen);
    }

    [Fact]
    public async Task DoppelteId_NurEinmal_UngueltigerUebergang409_MitStand()
    {
        var u = await AufbauAsync();
        var id = Guid.NewGuid().ToString();

        Assert.False(Json(await Senden(u, "starten", id)).GetProperty("doppelt").GetBoolean());
        var nochmal = await Senden(u, "starten", id);
        Assert.True(Json(nochmal).GetProperty("doppelt").GetBoolean());
        Assert.Single(u.Hinweise.Calls, c => c == nameof(IWarningService.AddWarning));

        var konflikt = await Senden(u, "weiter");
        Assert.IsType<ConflictObjectResult>(konflikt);
        Assert.Equal("laeuft", Status(konflikt));
        Assert.False(string.IsNullOrEmpty(Json(konflikt).GetProperty("error").GetString()));

        // Abgelehnte id wird nicht gemerkt: nach dem passenden Stand wirkt sie
        var spaeter = Guid.NewGuid().ToString();
        u.Team.PauseTimer();
        Assert.IsType<ConflictObjectResult>(await Senden(u, "pause", spaeter));
        Assert.Equal("laeuft", Status(await Senden(u, "weiter", spaeter)));
    }

    [Fact]
    public async Task EinstellungAus403_FalscheEingabe400_Token410_LageMitDarfSteuern()
    {
        var u = await AufbauAsync();
        Assert.True(Json(await Controller(u).GetLage()).GetProperty("suche").GetProperty("darfSteuern").GetBoolean());

        u.Einstellungen.AppTeamsSteuernSuche = false;
        Assert.Equal(403, ((ObjectResult)await Senden(u, "starten")).StatusCode);
        Assert.False(u.Team.IsRunning);
        Assert.False(Json(await Controller(u).GetLage()).GetProperty("suche").GetProperty("darfSteuern").GetBoolean());

        Assert.IsType<BadRequestObjectResult>(await Senden(u, "losrennen"));
        Assert.IsType<BadRequestObjectResult>(await Controller(u).PostSuche(new() { Id = null, Aktion = "starten" }));
        Assert.Equal(410, ((ObjectResult)await Controller(u, "falsch").PostSuche(new() { Id = "x", Aktion = "starten" })).StatusCode);
    }

    [Fact]
    public async Task Start_SchicktLageAnDieApp()
    {
        var u = await AufbauAsync();
        var (id, leser) = u.Ereignisse.Abonnieren(u.Team.TeamId);

        await Senden(u, "starten");

        var ereignisse = new List<string>();
        while (leser.TryRead(out var text)) ereignisse.Add(text);
        Assert.Contains(ereignisse, e => e.StartsWith("event: lage", StringComparison.Ordinal));
        u.Ereignisse.Abmelden(id);
    }
}

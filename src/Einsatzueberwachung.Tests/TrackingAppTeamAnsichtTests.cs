using System.Security.Cryptography;
using System.Text;
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

/// <summary>Native Team-Ansicht der App: lage, tracks, statusmeldung, ereignisse (#119, docs/APP_API.md 6–9)</summary>
public class TrackingAppTeamAnsichtTests
{
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(64);

    private sealed record Umgebung(EinsatzService Einsatz, TeamMobileTokenService Tokens, CollarTrackingService Halsbaender,
        TeamAppEreignisse Ereignisse, AppStatusmeldungen Status, Team Team, string Token);

    private static async Task<Umgebung> AufbauAsync(bool uebung = false)
    {
        var einsatz = new EinsatzService();
        var tokens = new TeamMobileTokenService(Secret, einsatz);
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", Stichwort = "Vermisste Person", IstEinsatz = !uebung, EinsatzDatum = new DateTime(2026, 10, 28, 9, 0, 0) });
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Team Rex", DogName = "Rex", HundefuehrerName = "Anna", HelferNames = ["Ben"] });
        var halsbaender = new CollarTrackingService(einsatz);
        var ereignisse = new TeamAppEreignisse(einsatz, halsbaender, tokens);
        return new Umgebung(einsatz, tokens, halsbaender, ereignisse, new AppStatusmeldungen(), team,
            tokens.IssueAppToken(team.TeamId, DateTime.UtcNow.AddHours(24)));
    }

    private static TeamMobileAppController Controller(Umgebung u, string? token = null, Stream? body = null)
    {
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) => m.Name switch
        {
            nameof(ISettingsService.GetAppSettingsAsync) => Task.FromResult(new AppSettings()),
            nameof(ISettingsService.GetStaffelSettingsAsync) => Task.FromResult(new StaffelSettings { StaffelName = "RHS Test" }),
            _ => null
        });
        var controller = new TeamMobileAppController(u.Tokens, u.Einsatz, settings.Proxy, NullLogger<TeamMobileAppController>.Instance,
            u.Halsbaender, u.Ereignisse, u.Status);
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer " + (token ?? u.Token);
        if (body is not null) http.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task Lage_TeamGebietHalsbandMitteilungen_ZeitenInUtc()
    {
        var u = await AufbauAsync();
        var gebiet = await u.Einsatz.AddSearchAreaAsync(new SearchArea { Name = "Gebiet A", Color = "#2f6fd6", Coordinates = [(49.31, 8.43), (49.32, 8.43), (49.32, 8.44)] });
        await u.Einsatz.AssignTeamToSearchAreaAsync(gebiet.Id, u.Team.TeamId);
        u.Team.CollarId = "HB-7";
        u.Team.CollarName = "Halsband 7";
        await u.Halsbaender.ReceiveLocationAsync("HB-7", "Halsband 7", 49.315, 8.435, 72, DateTime.UtcNow.AddSeconds(-12));
        u.Einsatz.TeamMitteilungSpeichern(u.Team.TeamId, "Bitte Funkkontakt");
        u.Team.StartTimer();

        var lage = Json(await Controller(u).GetLage());

        Assert.Equal("track", lage.GetProperty("mode").GetString());
        Assert.Equal("Vermisste Person · Testwald", lage.GetProperty("einsatz").GetProperty("text").GetString());
        Assert.False(lage.GetProperty("einsatz").GetProperty("uebung").GetBoolean());
        Assert.Equal("Anna", lage.GetProperty("team").GetProperty("hundefuehrerName").GetString());
        Assert.Equal("Ben", lage.GetProperty("team").GetProperty("helfer")[0].GetString());
        Assert.Equal("laeuft", lage.GetProperty("suche").GetProperty("status").GetString());
        Assert.EndsWith("Z", lage.GetProperty("suche").GetProperty("seit").GetString());
        Assert.Equal(3, lage.GetProperty("suchgebiet").GetProperty("punkte").GetArrayLength());
        Assert.Equal(72, lage.GetProperty("halsband").GetProperty("akku").GetInt32());
        Assert.EndsWith("Z", lage.GetProperty("halsband").GetProperty("zeit").GetString());
        var mitteilung = Assert.Single(lage.GetProperty("mitteilungen").EnumerateArray());
        Assert.Equal("Bitte Funkkontakt", mitteilung.GetProperty("text").GetString());
        Assert.EndsWith("Z", mitteilung.GetProperty("zeit").GetString());
        Assert.EndsWith("Z", lage.GetProperty("serverZeit").GetString());
    }

    [Fact]
    public async Task Lage_OhneGebietUndHalsband_StatusUndModus()
    {
        var u = await AufbauAsync(uebung: true);

        var lage = Json(await Controller(u).GetLage());
        Assert.Equal("bereit", lage.GetProperty("suche").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, lage.GetProperty("suche").GetProperty("seit").ValueKind);
        Assert.Equal(JsonValueKind.Null, lage.GetProperty("suchgebiet").ValueKind);
        Assert.Equal(JsonValueKind.Null, lage.GetProperty("halsband").ValueKind);
        Assert.True(lage.GetProperty("einsatz").GetProperty("uebung").GetBoolean());
        Assert.Equal("pause", lage.GetProperty("mode").GetString());

        u.Team.StartTimer(); u.Team.PauseTimer();
        Assert.Equal("kurzpause", Json(await Controller(u).GetLage()).GetProperty("suche").GetProperty("status").GetString());
        u.Team.StartTimer(); u.Team.StopTimer();
        Assert.Equal("beendet", Json(await Controller(u).GetLage()).GetProperty("suche").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Mitteilungen_Hoechstens20_UndUeberstehenNeustart()
    {
        var u = await AufbauAsync();
        for (var i = 1; i <= 25; i++) u.Einsatz.TeamMitteilungSpeichern(u.Team.TeamId, $"Nachricht {i}");
        u.Einsatz.TeamMitteilungSpeichern("anderes-team", "fremd");

        var liste = u.Einsatz.TeamMitteilungen(u.Team.TeamId);
        Assert.Equal(20, liste.Count);
        Assert.Equal("Nachricht 6", liste[0].Text);   // aelteste zuerst
        Assert.Equal("Nachricht 25", liste[^1].Text);

        // Neustart: Schnappschuss speichern und in einen neuen Dienst laden (wie RuntimeStatePersistenceService)
        var json = JsonSerializer.Serialize(u.Einsatz.ExportRuntimeSnapshot());
        var neu = new EinsatzService();
        await neu.ImportRuntimeSnapshotAsync(JsonSerializer.Deserialize<EinsatzRuntimeSnapshot>(json)!);
        Assert.Equal(20, neu.TeamMitteilungen(u.Team.TeamId).Count);
    }

    [Fact]
    public async Task Tracks_LaufendMitUnixMs_SeitFiltert_AbgeschlosseneOhneSeit()
    {
        var u = await AufbauAsync();
        u.Team.CollarId = "HB-7";
        await u.Halsbaender.AssignCollarToTeamAsync("HB-7", u.Team.TeamId);
        u.Team.StartTimer();
        var t0 = DateTime.UtcNow.AddMinutes(-5);
        await u.Halsbaender.ReceiveLocationAsync("HB-7", "HB 7", 49.31, 8.43, null, t0);
        await u.Halsbaender.ReceiveLocationAsync("HB-7", "HB 7", 49.32, 8.44, null, t0.AddMinutes(1));
        await u.Einsatz.UpdateTeamPhoneLocationAsync(u.Team.TeamId, 49.30, 8.42, 5, t0.AddSeconds(30));

        var alles = Json(Controller(u).GetTracks(null));
        var hund = alles.GetProperty("laufend").GetProperty("hund");
        Assert.Equal(2, hund.GetArrayLength());
        var tErster = new DateTimeOffset(t0).ToUnixTimeMilliseconds();
        Assert.InRange(hund[0].GetProperty("t").GetInt64(), tErster - 1000, tErster + 1000);
        Assert.Equal(1, alles.GetProperty("laufend").GetProperty("mensch").GetArrayLength());
        Assert.Equal(JsonValueKind.Array, alles.GetProperty("abgeschlossen").ValueKind);

        var neuer = Json(Controller(u).GetTracks(hund[0].GetProperty("t").GetInt64()));
        Assert.Equal(1, neuer.GetProperty("laufend").GetProperty("hund").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, neuer.GetProperty("abgeschlossen").ValueKind);
    }

    [Fact]
    public async Task Statusmeldung_GleicheIdNurEineNotiz_UnbekannterCode400()
    {
        var u = await AufbauAsync();
        var vorher = u.Einsatz.GlobalNotes.Count;

        var erste = Json(await Controller(u).PostStatusmeldung(new() { Id = "abc-1", Code = "im_gebiet" }));
        var zweite = Json(await Controller(u).PostStatusmeldung(new() { Id = "abc-1", Code = "im_gebiet" }));

        Assert.False(erste.GetProperty("doppelt").GetBoolean());
        Assert.True(zweite.GetProperty("doppelt").GetBoolean());
        Assert.Equal(vorher + 1, u.Einsatz.GlobalNotes.Count);
        Assert.Contains(u.Einsatz.GlobalNotes, n => n.Text == "[Team Rex] im Suchgebiet eingetroffen");

        Assert.IsType<BadRequestObjectResult>(await Controller(u).PostStatusmeldung(new() { Id = "abc-2", Code = "kaffee" }));
        Assert.IsType<BadRequestObjectResult>(await Controller(u).PostStatusmeldung(new() { Id = null, Code = "pause" }));
    }

    [Fact]
    public async Task AlleNeuenEndpunkte_NachEinsatzende410()
    {
        var u = await AufbauAsync();
        await u.Einsatz.EndEinsatzAsync();

        Assert.Equal(410, ((ObjectResult)await Controller(u).GetLage()).StatusCode);
        Assert.Equal(410, ((ObjectResult)Controller(u).GetTracks(null)).StatusCode);
        Assert.Equal(410, ((ObjectResult)await Controller(u).PostStatusmeldung(new() { Id = "x", Code = "pause" })).StatusCode);

        var body = new MemoryStream();
        var c = Controller(u, body: body);
        await c.GetEreignisse();
        Assert.Equal(410, c.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Ereignisse_HalsbandMitteilungLage_KeineFlutBeiTeamUpdated()
    {
        var u = await AufbauAsync();
        u.Team.CollarId = "HB-7";
        var (id, leser) = u.Ereignisse.Abonnieren(u.Team.TeamId);

        await u.Halsbaender.ReceiveLocationAsync("HB-7", "HB 7", 49.31, 8.43, 50, DateTime.UtcNow);
        u.Einsatz.TeamMitteilungSpeichern(u.Team.TeamId, "Kommen");
        await u.Einsatz.UpdateTeamAsync(u.Team);            // nichts Relevantes geaendert → kein „lage“
        u.Team.StartTimer();
        await u.Einsatz.UpdateTeamAsync(u.Team);            // Suche laeuft → „lage“

        var ereignisse = new List<string>();
        while (leser.TryRead(out var e)) ereignisse.Add(e);

        Assert.Equal(["halsband", "mitteilung", "lage"], ereignisse.Select(e => e.Split('\n')[0]["event: ".Length..]));
        Assert.All(ereignisse, e => Assert.EndsWith("\n\n", e));
        Assert.All(ereignisse, e => Assert.Equal(4, e.Split('\n').Length)); // „event“, einzeiliges „data“, Leerzeile, Ende
        u.Ereignisse.Abmelden(id);
    }

    [Fact]
    public async Task Ereignisse_Endpunkt_RetryPingUndStopBeiEinsatzende()
    {
        var u = await AufbauAsync();
        var vorher = TeamMobileAppController.PingIntervall;
        TeamMobileAppController.PingIntervall = TimeSpan.FromMilliseconds(100);
        try
        {
            var body = new MemoryStream();
            var c = Controller(u, body: body);
            var lauf = c.GetEreignisse();
            await Task.Delay(350);                           // mindestens ein Ping
            await u.Einsatz.EndEinsatzAsync();               // → stop, Verbindung zu
            await lauf.WaitAsync(TimeSpan.FromSeconds(5));

            var text = Encoding.UTF8.GetString(body.ToArray());
            Assert.Equal("text/event-stream", c.HttpContext.Response.ContentType);
            Assert.StartsWith("retry: 5000\n\n", text);
            Assert.Contains(": ping\n\n", text);
            Assert.EndsWith("event: stop\ndata: {}\n\n", text);
            Assert.Equal(0, u.Ereignisse.AnzahlVerbindungen);
        }
        finally
        {
            TeamMobileAppController.PingIntervall = vorher;
        }
    }
}

/// <summary>Befunde der Leitung zu PR #122</summary>
public class TrackingAppTeamAnsichtReviewTests
{
    private static readonly byte[] Secret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(64);

    [Fact]
    public async Task HoechstensDreiVerbindungenJeTeam_AeltesteBekommtStop()
    {
        var einsatz = new EinsatzService();
        var tokens = new TeamMobileTokenService(Secret, einsatz);
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 28, 9, 0, 0) });
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Team Rex" });
        var ereignisse = new TeamAppEreignisse(einsatz, new CollarTrackingService(einsatz), tokens);

        var (_, aeltester) = ereignisse.Abonnieren(team.TeamId);
        for (var i = 0; i < TeamAppEreignisse.MaxVerbindungenJeTeam; i++) ereignisse.Abonnieren(team.TeamId);

        Assert.Equal(TeamAppEreignisse.MaxVerbindungenJeTeam, ereignisse.AnzahlVerbindungen);
        Assert.True(aeltester.TryRead(out var letztes));
        Assert.StartsWith("event: stop", letztes);
        Assert.True(aeltester.Completion.IsCompleted);
    }

    [Fact]
    public async Task OffeneVerbindung_EndetBeiAblaufDesTokens()
    {
        var einsatz = new EinsatzService();
        var tokens = new TeamMobileTokenService(Secret, einsatz);
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 28, 9, 0, 0) });
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Team Rex" });
        var halsbaender = new CollarTrackingService(einsatz);
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((_, _) => null);
        var controller = new TeamMobileAppController(tokens, einsatz, settings.Proxy, NullLogger<TeamMobileAppController>.Instance,
            halsbaender, new TeamAppEreignisse(einsatz, halsbaender, tokens), new AppStatusmeldungen());
        var http = new DefaultHttpContext();
        // Token laeuft in 2 s ab (ganze Sekunden im Token)
        http.Request.Headers.Authorization = "Bearer " + tokens.IssueAppToken(team.TeamId, DateTime.UtcNow.AddSeconds(2));
        var body = new MemoryStream();
        http.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        var vorher = TeamMobileAppController.PingIntervall;
        TeamMobileAppController.PingIntervall = TimeSpan.FromMilliseconds(200);
        try
        {
            await controller.GetEreignisse().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            TeamMobileAppController.PingIntervall = vorher;
        }
        Assert.EndsWith("event: stop\ndata: {}\n\n", Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact]
    public async Task Mitteilungen_ListeWirdErsetztNichtVeraendert()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 28, 9, 0, 0) });
        var vorher = einsatz.CurrentEinsatz.TeamMitteilungen;

        einsatz.TeamMitteilungSpeichern("t1", "Hallo");

        Assert.Empty(vorher); // eine laufende Speicherung sieht weiter ihre unveraenderte Liste
        Assert.Single(einsatz.CurrentEinsatz.TeamMitteilungen);
    }
}

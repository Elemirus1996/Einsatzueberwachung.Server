using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Einsatzueberwachung.Server.Services.Radio;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Native Einsatzleitung, Stufe 3: Team-Aktionen, Notizen, Funk, Mitteilung (#125)</summary>
public sealed class ElAppAktionenTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-elaktion-" + Guid.NewGuid().ToString("N"));
    private readonly LeitungAuthStore _leitung;
    private readonly EinsatzService _einsatz = new();
    private readonly CollarTrackingService _halsbaender;
    private readonly ElAppTokenService _tokens;
    private readonly ElAppAktionen _aktionen = new();
    private readonly EinsatzAbschlussTests.Recorder<IAuditLogService> _audit = EinsatzAbschlussTests.Recorder<IAuditLogService>.Create((_, _) => null);
    private readonly EinsatzAbschlussTests.Recorder<IRadioService> _funk;
    private readonly string _token;

    private sealed class Uhr : ITimeService
    {
        public DateTime Now => Ortszeit.Jetzt;
        public void Refresh() { }
    }

    public ElAppAktionenTests()
    {
        Directory.CreateDirectory(_dir);
        _leitung = new LeitungAuthStore(_dir);
        Assert.Null(_leitung.Setup(_leitung.SetupCode!, "Rettungshund2026", "Rettungshund2026"));
        _halsbaender = new CollarTrackingService(_einsatz);
        _tokens = new ElAppTokenService(RandomNumberGenerator.GetBytes(64), _leitung);
        _token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(1));
        // Wie der RadioService: Funkspruch zusaetzlich als Einsatz-Notiz mit Quelle „Funk“
        _funk = EinsatzAbschlussTests.Recorder<IRadioService>.Create((m, args) =>
        {
            if (m.Name != nameof(IRadioService.AddMessageAsync)) return null;
            var anfrage = (CreateRadioMessageRequest)args![0]!;
            var notiz = _einsatz.AddGlobalNoteWithSourceAsync(anfrage.Text, anfrage.SourceTeamId, anfrage.SourceTeamName, "Funk",
                GlobalNotesEntryType.Manual, anfrage.CreatedBy).Result;
            return Task.FromResult(new RadioMessageDto("r1", DateTime.UtcNow, anfrage.Text, anfrage.SourceTeamId, anfrage.SourceTeamName,
                anfrage.CreatedBy, [], "") { NotizId = notiz.Id });
        });
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ElAppController Controller(string? token = null, int port = 5000)
    {
        var positionen = new ElPositionService(_einsatz);
        var c = new ElAppController(_tokens, positionen, new LeitungsGeraetNamen(new VpnDeviceStore(_dir)),
            new PublicAccess(5002), NullLogger<ElAppController>.Instance, null,
            _leitung, new LoginLockout(), _audit.Proxy, _einsatz, _halsbaender, null, null, null,
            _aktionen, new TeamSucheService(_einsatz, _halsbaender, new Uhr()), _funk.Proxy, new TeamMobileTokenService(new byte[64], _einsatz));
        var http = new DefaultHttpContext();
        http.Connection.LocalPort = port;
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.9");
        http.Request.Headers.Authorization = "Bearer " + (token ?? _token);
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static ElAppController.ElAktion Neu(string? halsbandId = null, string? text = null, string? teamId = null, string? antwortAuf = null) =>
        new(Guid.NewGuid().ToString(), halsbandId, text, teamId, antwortAuf);

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    private static string Status(IActionResult r) => Json(Assert.IsType<OkObjectResult>(r)).GetProperty("suche").GetProperty("status").GetString()!;

    private async Task<Team> EinsatzMitTeamAsync(string name = "Team Rex")
    {
        if (!ElPositionService.Laeuft(_einsatz.CurrentEinsatz))
            await _einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = new DateTime(2026, 10, 29, 9, 0, 0) });
        return await _einsatz.AddTeamAsync(new Team { TeamName = name, DogName = "Rex" });
    }

    [Fact]
    public async Task TeamAktionen_Ablauf_Doppelt_Konflikte()
    {
        var team = await EinsatzMitTeamAsync();
        await _halsbaender.ReceiveLocationAsync("HB-7", "Halsband 7", 49.31, 8.43, 80, DateTime.UtcNow);
        var start = Neu(halsbandId: "HB-7");

        Assert.Equal("laeuft", Status(await Controller().TeamStart(team.TeamId, start)));
        Assert.Equal("HB-7", team.CollarId);
        Assert.True(Json(await Controller().TeamStart(team.TeamId, start)).GetProperty("doppelt").GetBoolean());
        Assert.IsType<ConflictObjectResult>(await Controller().TeamStart(team.TeamId, Neu()));
        Assert.IsType<ConflictObjectResult>(await Controller().TeamZuruecksetzen(team.TeamId, Neu()));

        Assert.Equal("kurzpause", Status(await Controller().TeamPause(team.TeamId, Neu())));
        Assert.IsType<ConflictObjectResult>(await Controller().TeamPause(team.TeamId, Neu()));
        Assert.Equal("laeuft", Status(await Controller().TeamWeiter(team.TeamId, Neu())));
        Assert.Equal("beendet", Status(await Controller().TeamStopp(team.TeamId, Neu())));
        Assert.Null(team.CollarId); // Stopp gibt das Halsband frei (wie /el)
        Assert.Equal("bereit", Status(await Controller().TeamZuruecksetzen(team.TeamId, Neu())));

        Assert.Equal(5, _audit.Calls.Count(c => c == nameof(IAuditLogService.Log))); // nur ausgefuehrte Aktionen
    }

    [Fact]
    public async Task Start_MitVergebenemHalsband409_UnbekanntesTeam404()
    {
        var rex = await EinsatzMitTeamAsync();
        var bello = await EinsatzMitTeamAsync("Team Bello");
        await _halsbaender.ReceiveLocationAsync("HB-7", "Halsband 7", 49.31, 8.43, 80, DateTime.UtcNow);
        await _halsbaender.AssignCollarToTeamAsync("HB-7", bello.TeamId);

        var konflikt = await Controller().TeamStart(rex.TeamId, Neu(halsbandId: "HB-7"));
        Assert.IsType<ConflictObjectResult>(konflikt);
        Assert.False(rex.IsRunning);
        Assert.IsType<NotFoundObjectResult>(await Controller().TeamPause("gibt-es-nicht", Neu()));
    }

    [Fact]
    public async Task Schutz_Oeffentlich404_Token410_IdFehlt400_OhneEinsatz409()
    {
        Assert.IsType<ConflictObjectResult>(await Controller().NotizAnlegen(Neu(text: "x")));
        var team = await EinsatzMitTeamAsync();
        Assert.IsType<NotFoundResult>(await Controller(port: 5002).TeamStart(team.TeamId, Neu()));
        Assert.Equal(410, ((ObjectResult)await Controller("e2.falsch").TeamStart(team.TeamId, Neu())).StatusCode);
        Assert.IsType<BadRequestObjectResult>(await Controller().TeamStart(team.TeamId, new ElAppController.ElAktion(null)));
        Assert.IsType<BadRequestObjectResult>(await Controller().NotizAnlegen(Neu(text: " ")));
        Assert.False(team.IsRunning);
    }

    [Fact]
    public async Task Notizen_AnlegenLoeschenUndListe_MitFunkUndAntworten()
    {
        var team = await EinsatzMitTeamAsync();

        var notiz = Json(await Controller().NotizAnlegen(Neu(text: "Presse vor Ort", teamId: team.TeamId))).GetProperty("notizId").GetString()!;
        Assert.Equal("[Team Rex] Presse vor Ort", _einsatz.CurrentEinsatz.ElNotizen.Single(n => n.Id == notiz).Text);

        var funk = Json(await Controller().Funk(Neu(text: "Bitte melden", teamId: team.TeamId))).GetProperty("notizId").GetString()!;
        Assert.Equal("@Team Rex Bitte melden", _einsatz.GlobalNotes.Single(n => n.Id == funk).Text);
        Assert.IsType<OkObjectResult>(await Controller().Funk(Neu(text: "Verstanden", antwortAuf: funk)));
        Assert.IsType<NotFoundObjectResult>(await Controller().Funk(Neu(text: "?", antwortAuf: "gibt-es-nicht")));

        var liste = Json(Controller().GetNotizen(null));
        var eintraege = liste.GetProperty("notizen").EnumerateArray().ToList();
        var f = eintraege.Single(e => e.GetProperty("id").GetString() == funk);
        Assert.Equal("funk", f.GetProperty("art").GetString());
        Assert.False(f.GetProperty("loeschbar").GetBoolean());
        Assert.Equal("Verstanden", f.GetProperty("antworten")[0].GetProperty("text").GetString());
        Assert.True(eintraege.Single(e => e.GetProperty("id").GetString() == notiz).GetProperty("loeschbar").GetBoolean());
        Assert.All(eintraege, e => Assert.EndsWith("Z", e.GetProperty("zeit").GetString()));

        // Blaettern: nach dem ersten Eintrag kommt der Rest
        var erste = eintraege[0].GetProperty("id").GetString();
        var weiter = Json(Controller().GetNotizen(erste, 50)).GetProperty("notizen").EnumerateArray().ToList();
        Assert.Equal(eintraege.Count - 1, weiter.Count);
        Assert.True(Json(Controller().GetNotizen(null, 1)).GetProperty("weitere").GetBoolean());

        Assert.IsType<NotFoundObjectResult>(await Controller().NotizLoeschen(funk, Neu())); // Funk bleibt im Protokoll
        Assert.IsType<OkObjectResult>(await Controller().NotizLoeschen(notiz, Neu()));
        Assert.DoesNotContain(_einsatz.CurrentEinsatz.ElNotizen, n => n.Id == notiz);
    }

    [Fact]
    public async Task Mitteilung_AnsTeam_GespeichertMitKennung()
    {
        var team = await EinsatzMitTeamAsync();

        var antwort = Json(await Controller().TeamMitteilen(team.TeamId, Neu(text: "Bitte Funkkontakt")));

        var gespeichert = Assert.Single(_einsatz.TeamMitteilungen(team.TeamId));
        Assert.Equal("Bitte Funkkontakt", gespeichert.Text);
        Assert.Equal(gespeichert.Id, antwort.GetProperty("mitteilungId").GetString());
    }
}

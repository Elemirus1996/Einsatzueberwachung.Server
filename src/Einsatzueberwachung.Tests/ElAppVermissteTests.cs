using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Native Einsatzleitung, Stufe 4a: Vermisste mit version, Checkliste, Szenario (#125)</summary>
public sealed class ElAppVermissteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-elverm-" + Guid.NewGuid().ToString("N"));
    private readonly LeitungAuthStore _leitung;
    private readonly EinsatzService _einsatz = new();
    private readonly ElAppTokenService _tokens;
    private readonly ElAppAktionen _aktionen = new();
    private readonly string _token;

    public ElAppVermissteTests()
    {
        Directory.CreateDirectory(_dir);
        _leitung = new LeitungAuthStore(_dir);
        Assert.Null(_leitung.Setup(_leitung.SetupCode!, "Rettungshund2026", "Rettungshund2026"));
        _tokens = new ElAppTokenService(RandomNumberGenerator.GetBytes(64), _leitung);
        _token = _tokens.Ausstellen(DateTime.UtcNow.AddHours(1));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ElAppController Controller()
    {
        var c = new ElAppController(_tokens, new ElPositionService(_einsatz), new LeitungsGeraetNamen(new VpnDeviceStore(_dir)),
            new PublicAccess(5002), NullLogger<ElAppController>.Instance, null,
            _leitung, new LoginLockout(), null, _einsatz, null, null, null, null, _aktionen);
        var http = new DefaultHttpContext();
        http.Connection.LocalPort = 5000;
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.10.0.9");
        http.Request.Headers.Authorization = "Bearer " + _token;
        c.ControllerContext = new ControllerContext { HttpContext = http };
        return c;
    }

    private static JsonObject Koerper(object daten) => JsonNode.Parse(JsonSerializer.Serialize(daten))!.AsObject();

    private static string NeueId() => Guid.NewGuid().ToString();

    private static JsonElement Json(IActionResult r) => JsonSerializer.SerializeToElement(((ObjectResult)r).Value);

    private Task StartAsync(EinsatzSzenarioType szenario = EinsatzSzenarioType.Flaeche) =>
        _einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, Szenario = szenario, EinsatzDatum = new DateTime(2026, 10, 29, 9, 0, 0) });

    [Fact]
    public async Task Anlegen_Aendern_VersionKonflikt409_MitAktuellemStand()
    {
        await StartAsync();
        var neu = Json(await Controller().VermissteSpeichern(Koerper(new
        {
            id = NeueId(), vorname = "Erna", nachname = "Muster", alter = "82", mobilitaet = "Rollator", polizeiVermisstenmeldungAufgenommen = true
        })));
        var profilId = neu.GetProperty("profilId").GetString()!;
        var version = neu.GetProperty("version").GetString()!;

        var profil = Assert.Single(Json(Controller().GetVermisste()).GetProperty("profile").EnumerateArray());
        Assert.Equal("Erna", profil.GetProperty("vorname").GetString());
        Assert.Equal("Rollator", profil.GetProperty("mobilitaet").GetString());
        Assert.True(profil.GetProperty("polizeiVermisstenmeldungAufgenommen").GetBoolean());
        Assert.Equal(version, profil.GetProperty("version").GetString());
        Assert.EndsWith("Z", profil.GetProperty("zuletztAktualisiert").GetString());

        // Inzwischen speichert /el im Browser (Auto-Save)
        var imBrowser = ElVermisste.Kopie(_einsatz.CurrentEinsatz.Vermisste[0]);
        imBrowser.Kleidung = "rote Jacke";
        await Task.Delay(5); // Zeitstempel aendert sich
        await _einsatz.UpsertVermisstenAsync(imBrowser);

        var konflikt = await Controller().VermissteSpeichern(Koerper(new { id = NeueId(), profilId, version, kleidung = "blau" }));
        Assert.IsType<ConflictObjectResult>(konflikt);
        var stand = Json(konflikt).GetProperty("profil");
        Assert.Equal("rote Jacke", stand.GetProperty("kleidung").GetString());

        var ok = Json(await Controller().VermissteSpeichern(Koerper(new
        {
            id = NeueId(), profilId, version = stand.GetProperty("version").GetString(), besonderheiten = "Hörgerät"
        })));
        Assert.NotEqual(stand.GetProperty("version").GetString(), ok.GetProperty("version").GetString());
        var gespeichert = _einsatz.CurrentEinsatz.Vermisste[0];
        Assert.Equal("Hörgerät", gespeichert.Besonderheiten);
        Assert.Equal("rote Jacke", gespeichert.Kleidung); // nicht mitgeschickt = unveraendert
    }

    [Fact]
    public async Task Fehler_OhneVersion400_UnbekanntesFeld400_ZweitePersonImMantrailer409_Entfernen()
    {
        await StartAsync(EinsatzSzenarioType.Mantrailer);
        var profilId = Json(await Controller().VermissteSpeichern(Koerper(new { id = NeueId(), vorname = "Erna" })))
            .GetProperty("profilId").GetString()!;

        Assert.IsType<BadRequestObjectResult>(await Controller().VermissteSpeichern(Koerper(new { id = NeueId(), profilId, vorname = "X" })));
        Assert.IsType<BadRequestObjectResult>(await Controller().VermissteSpeichern(Koerper(new { id = NeueId(), lieblingsfarbe = "blau" })));
        Assert.IsType<BadRequestObjectResult>(await Controller().VermissteSpeichern(Koerper(new { id = NeueId(), mobilitaet = "Fliegen" })));
        Assert.IsType<ConflictObjectResult>(await Controller().VermissteSpeichern(Koerper(new { id = NeueId(), vorname = "Zweite" })));

        Assert.IsType<OkObjectResult>(await Controller().VermissteEntfernen(profilId, new ElAppController.ElAktion(NeueId())));
        Assert.Empty(_einsatz.CurrentEinsatz.Vermisste);
        Assert.IsType<NotFoundObjectResult>(await Controller().VermissteEntfernen(profilId, new ElAppController.ElAktion(NeueId())));
    }

    [Fact]
    public async Task Checkliste_JaNeinUndAuswahl_OhneVersion()
    {
        await StartAsync();
        var jaNein = new ChecklistItemDefinition { Label = "Geruchsstück gesichert", Type = ChecklistItemType.Bool };
        var auswahl = new ChecklistItemDefinition { Label = "Stockwerk", Type = ChecklistItemType.Choice, Choices = ["EG", "1. OG"] };
        await _einsatz.UpsertVermisstenAsync(new VermisstenInfo
        {
            Vorname = "Erna",
            Checkliste = new ChecklistInstance { TemplateName = "Test", Items = [jaNein, auswahl] }
        });
        var profilId = _einsatz.CurrentEinsatz.Vermisste[0].Id.ToString();

        Assert.IsType<OkObjectResult>(await Controller().VermissteCheckliste(profilId, new(NeueId(), jaNein.Id.ToString(), Erledigt: true)));
        Assert.IsType<OkObjectResult>(await Controller().VermissteCheckliste(profilId, new(NeueId(), auswahl.Id.ToString(), Wert: "1. OG")));
        Assert.IsType<BadRequestObjectResult>(await Controller().VermissteCheckliste(profilId, new(NeueId(), auswahl.Id.ToString(), Wert: "Dach")));
        Assert.IsType<NotFoundObjectResult>(await Controller().VermissteCheckliste(profilId, new(NeueId(), NeueId(), Erledigt: true)));

        var punkte = Json(Controller().GetVermisste()).GetProperty("profile")[0].GetProperty("checkliste").GetProperty("punkte");
        Assert.True(punkte[0].GetProperty("erledigt").GetBoolean());
        Assert.Equal("jaNein", punkte[0].GetProperty("typ").GetString());
        Assert.Equal("1. OG", punkte[1].GetProperty("wert").GetString());
    }

    [Fact]
    public async Task Szenario_Setzen_UnbekanntOderZahl400()
    {
        await StartAsync(EinsatzSzenarioType.Unbestimmt);
        Assert.IsType<OkObjectResult>(await Controller().SzenarioSetzen(new(NeueId(), "Truemmer")));
        Assert.Equal(EinsatzSzenarioType.Truemmer, _einsatz.CurrentEinsatz.Szenario);
        Assert.IsType<BadRequestObjectResult>(await Controller().SzenarioSetzen(new(NeueId(), "2")));
        Assert.IsType<BadRequestObjectResult>(await Controller().SzenarioSetzen(new(NeueId(), "Wasser")));
        Assert.Equal("Truemmer", Json(Controller().GetVermisste()).GetProperty("szenario").GetString());
    }
}

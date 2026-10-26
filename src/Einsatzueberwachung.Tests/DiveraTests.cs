using System.Net;
using System.Text;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Divera;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Controllers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

public class DiveraTests
{
    // Status der Einheit: gruen „Komme“, gelb „30 Minuten“, rot „Nicht einsatzbereit“, grau „Info“
    private const string PullJson = """
    {
      "success": true,
      "data": {
        "cluster": {
          "status_sorter": {
            "101": { "id": 101, "name": "Komme", "color": "2ECC40" },
            "102": { "id": 102, "name": "30 Minuten", "color": "FFDC00" },
            "103": { "id": 103, "name": "Nicht einsatzbereit", "color": "FF4136" },
            "104": { "id": 104, "name": "Info", "color": "AAAAAA" }
          },
          "status": { "1": 101, "2": 102 }
        },
        "cluster_members": {
          "1": { "id": 1, "firstname": "Anna", "lastname": "Divera" },
          "2": { "id": 2, "firstname": "Ben", "lastname": "Divera" }
        },
        "alarm": {
          "items": {
            "500": { "id": 500, "title": "Vermisste Person", "address": "Waldweg", "closed": false,
                     "ucr": { "1": 101, "2": 102, "3": 103, "4": 0, "5": 104 } }
          }
        }
      }
    }
    """;

    [Theory]
    [InlineData("2ECC40", DiveraRueckmeldung.Kommt)]
    [InlineData("#FFDC00", DiveraRueckmeldung.KommtSpaeter)]
    [InlineData("FFA500", DiveraRueckmeldung.KommtSpaeter)]
    [InlineData("FF4136", DiveraRueckmeldung.KommtNicht)]
    [InlineData("AAAAAA", DiveraRueckmeldung.Unbekannt)]
    [InlineData("", DiveraRueckmeldung.Unbekannt)]
    public void Bedeutung_AusFarbe(string farbe, DiveraRueckmeldung erwartet)
    {
        Assert.Equal(erwartet, DiveraStatusFarbe.Bedeutung(farbe));
    }

    [Fact]
    public void Bedeutung_ZuordnungSchlaegtFarbe_AlteIdsAlsLetzterAusweg()
    {
        var zuordnung = new Dictionary<int, DiveraRueckmeldung> { [104] = DiveraRueckmeldung.Kommt };

        Assert.Equal(DiveraRueckmeldung.Kommt, DiveraService.BedeutungFuer(104, "AAAAAA", zuordnung));
        Assert.Equal(DiveraRueckmeldung.KommtNicht, DiveraService.BedeutungFuer(103, "FF4136", zuordnung));
        Assert.Equal(DiveraRueckmeldung.KommtSpaeter, DiveraService.BedeutungFuer(3, null, zuordnung));
        Assert.Equal(DiveraRueckmeldung.Unbekannt, DiveraService.BedeutungFuer(56296, null, zuordnung));
    }

    [Theory]
    [InlineData(1, false, 15)]
    [InlineData(2, false, 30)]
    [InlineData(3, false, 60)]
    [InlineData(10, false, 300)]
    [InlineData(1, true, 60)]
    public void Backoff_WaechstUndRespektiert429(int fehler, bool zuViele, int sekunden)
    {
        Assert.Equal(TimeSpan.FromSeconds(sekunden), DiveraService.BackoffDelay(fehler, zuViele, null));
    }

    [Fact]
    public void Backoff_RetryAfterVonDiveraGiltBei429()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), DiveraService.BackoffDelay(1, true, TimeSpan.FromSeconds(120)));
    }

    [Fact]
    public async Task PullAll_NamenAusStammdaten_StatusDerEinheit_GruppiertNachBedeutung()
    {
        var http = new FakeDivera();
        http.Antwort("/api/v2/pull/all", PullJson);
        var service = Service(http, personal: [new PersonalEntry { Vorname = "Anna", Nachname = "Stammdaten", DiveraUserId = 1 }]);

        var alarm = Assert.Single(await service.GetActiveAlarmsAsync());

        var namen = alarm.UcrDetails.ToDictionary(u => u.MemberId, u => u.MemberName);
        Assert.Equal("Anna Stammdaten", namen[1]); // Stammdaten vor Divera
        Assert.Equal("Ben Divera", namen[2]);      // sonst Name aus Divera
        Assert.Equal("#3", namen[3]);              // unbekannt

        Assert.Equal(4, alarm.CountKomme);
        Assert.Equal(1, alarm.CountKeineAntwort);
        Assert.Equal(2, alarm.CountZusagen);
        Assert.Equal(
            ["Komme", "30 Minuten", "Nicht einsatzbereit", "Info"],
            alarm.Rueckmeldungen.Select(g => g.Name));
        Assert.Equal("bg-warning text-dark", alarm.Rueckmeldungen[1].BadgeCss);
    }

    [Fact]
    public async Task PullAll_GleichzeitigeSeitenTeilenEineAbfrage()
    {
        var http = new FakeDivera { Verzoegerung = TimeSpan.FromMilliseconds(100) };
        http.Antwort("/api/v2/pull/all", PullJson);
        var service = Service(http);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => service.PullAllAsync()));

        Assert.Equal(1, http.Aufrufe("/api/v2/pull/all"));
    }

    [Fact]
    public async Task PullAll_NachFehlerWartetBackoffAb()
    {
        var http = new FakeDivera();
        http.Antwort("/api/v2/pull/all", "{}", HttpStatusCode.TooManyRequests);
        var service = Service(http);

        Assert.Null(await service.PullAllAsync());
        Assert.Null(await service.PullAllAsync());

        Assert.Equal(1, http.Aufrufe("/api/v2/pull/all"));
    }

    [Fact]
    public void FindeKanalId_SuchtMessageChannelAuchVerschachtelt()
    {
        using var flach = JsonDocument.Parse("""{ "id": 500, "message_channel_id": 77 }""");
        using var tief = JsonDocument.Parse("""{ "id": 500, "chat": { "message_channel": { "id": 1 }, "message_channel_id": 88 } }""");
        using var anders = JsonDocument.Parse("""{ "id": 500, "channel_id": 99 }""");
        using var keins = JsonDocument.Parse("""{ "id": 500, "title": "x" }""");

        Assert.Equal(77, DiveraService.FindeKanalId(flach.RootElement));
        Assert.Equal(88, DiveraService.FindeKanalId(tief.RootElement));
        Assert.Equal(99, DiveraService.FindeKanalId(anders.RootElement));
        Assert.Null(DiveraService.FindeKanalId(keins.RootElement));
    }

    [Fact]
    public async Task Foto_GehtAlsNachrichtMitAnhangInDenEinsatzChat()
    {
        var http = new FakeDivera();
        http.Antwort("/api/v2/alarms/500", """{ "success": true, "data": { "id": 500, "message_channel_id": 77 } }""");
        http.Antwort("/api/v2/messages", """{ "success": true, "data": { "id": 9001 } }""");
        http.Antwort("/api/v2/messages/attachment/9001", """{ "success": true }""");
        var service = Service(http, fotos: true);

        var ergebnis = await service.SendeFotoAnEinsatzChatAsync(500, [0xFF, 0xD8, 0xFF, 0x00], "EL.jpg", "image/jpeg", "Fundort");

        Assert.True(ergebnis.Erfolg, ergebnis.Meldung);
        var nachricht = http.Koerper("/api/v2/messages");
        Assert.Contains("\"message_channel_id\":77", nachricht);
        Assert.Contains("Fundort", nachricht);
        Assert.Contains("Attachment[upload]", http.Koerper("/api/v2/messages/attachment/9001"));
    }

    [Fact]
    public async Task Foto_OhneEinsatzChatOderAusgeschaltetMitKlarerMeldung()
    {
        var http = new FakeDivera();
        http.Antwort("/api/v2/alarms/500", """{ "success": true, "data": { "id": 500 } }""");

        var aus = await Service(http, fotos: false).SendeFotoAnEinsatzChatAsync(500, [1], "a.jpg", "image/jpeg", null);
        var ohneKanal = await Service(http, fotos: true).SendeFotoAnEinsatzChatAsync(500, [1], "a.jpg", "image/jpeg", null);

        Assert.False(aus.Erfolg);
        Assert.Contains("nicht eingeschaltet", aus.Meldung);
        Assert.False(ohneKanal.Erfolg);
        Assert.Contains("kein Messenger-Chat", ohneKanal.Meldung);
        Assert.Equal(0, http.Aufrufe("/api/v2/messages"));
    }

    [Fact]
    public void FotoEndpunkt_AlarmDesEinsatzesUndBildformat()
    {
        var offen = new List<DiveraAlarm> { new() { Id = 7 } };
        Assert.Equal(5, DiveraController.EinsatzAlarmId(5, offen));
        Assert.Equal(7, DiveraController.EinsatzAlarmId(null, offen));
        Assert.Null(DiveraController.EinsatzAlarmId(null, [new() { Id = 7 }, new() { Id = 8 }]));

        Assert.Equal("image/jpeg", DiveraController.BildFormat([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/png", DiveraController.BildFormat([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.Equal("image/webp", DiveraController.BildFormat(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ")));
        Assert.Null(DiveraController.BildFormat(Encoding.ASCII.GetBytes("<svg onload=alert(1)>")));
    }

    private static DiveraService Service(FakeDivera http, List<PersonalEntry>? personal = null, bool fotos = false)
    {
        var settings = new AppSettings
        {
            DiveraEnabled = true,
            DiveraAccessKey = "test-key",
            DiveraBaseUrl = "https://divera.test/api/v2",
            DiveraFotosSenden = fotos
        };
        var settingsService = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) =>
            m.Name == nameof(ISettingsService.GetAppSettingsAsync) ? Task.FromResult(settings) : null);
        var masterData = EinsatzAbschlussTests.Recorder<IMasterDataService>.Create((m, _) =>
            m.Name == nameof(IMasterDataService.GetPersonalListAsync) ? Task.FromResult(personal ?? new List<PersonalEntry>()) : null);
        return new DiveraService(settingsService.Proxy, masterData.Proxy, NullLogger<DiveraService>.Instance, new HttpClient(http));
    }

    /// <summary>Nachgebautes Divera: feste Antworten je Pfad, zaehlt Aufrufe und merkt sich die Anfrage-Koerper.</summary>
    private sealed class FakeDivera : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Json, HttpStatusCode Status)> _antworten = new();
        private readonly Dictionary<string, int> _aufrufe = new();
        private readonly Dictionary<string, string> _koerper = new();
        public TimeSpan Verzoegerung { get; init; }

        public void Antwort(string pfad, string json, HttpStatusCode status = HttpStatusCode.OK) => _antworten[pfad] = (json, status);
        public int Aufrufe(string pfad) { lock (_aufrufe) return _aufrufe.GetValueOrDefault(pfad); }
        public string Koerper(string pfad) => _koerper.GetValueOrDefault(pfad, string.Empty);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var pfad = request.RequestUri!.AbsolutePath;
            Assert.Contains("accesskey=test-key", request.RequestUri.Query);
            lock (_aufrufe) _aufrufe[pfad] = _aufrufe.GetValueOrDefault(pfad) + 1;
            if (request.Content != null) _koerper[pfad] = await request.Content.ReadAsStringAsync(cancellationToken);
            if (Verzoegerung > TimeSpan.Zero) await Task.Delay(Verzoegerung, cancellationToken);
            var (json, status) = _antworten.TryGetValue(pfad, out var a) ? a : ("{}", HttpStatusCode.NotFound);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

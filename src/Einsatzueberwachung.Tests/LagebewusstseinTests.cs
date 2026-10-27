using System.Net;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Handy-Funkstille/-Akku, Systemzustand, Leitungsplatz, Traccar-Test (1.11.5)</summary>
public class LagebewusstseinTests
{
    private static readonly DateTime Jetzt = new(2026, 10, 27, 16, 0, 0);

    private sealed class FesteZeit(DateTime now) : ITimeService
    {
        public DateTime Now { get; set; } = now;
        public void Refresh() { }
    }

    private static (TeamHandyUeberwachungService Dienst, List<WarningEntry> Warnungen) Ueberwachung(EinsatzService einsatz, FesteZeit zeit)
    {
        var warnungen = new List<WarningEntry>();
        var warnService = EinsatzAbschlussTests.Recorder<IWarningService>.Create((m, args) => m.Name switch
        {
            nameof(IWarningService.AddWarning) => Merke(warnungen, (WarningEntry)args![0]!),
            nameof(IWarningService.GetRuleConfig) => new WarningRuleConfig(),
            _ => null
        });
        var settings = new AppSettings();
        var settingsService = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) =>
            m.Name == nameof(ISettingsService.GetAppSettingsAsync) ? Task.FromResult(settings) : null);
        return (new TeamHandyUeberwachungService(einsatz, settingsService.Proxy, warnService.Proxy, zeit,
            NullLogger<TeamHandyUeberwachungService>.Instance), warnungen);
    }

    private static object? Merke(List<WarningEntry> liste, WarningEntry eintrag)
    {
        liste.Add(eintrag);
        return null;
    }

    [Fact]
    public async Task Funkstille_NurBeiTeamsMitHandy_UndErstNachDemZeitfenster()
    {
        var einsatz = new EinsatzService();
        var mitHandy = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });
        var ohneHandy = await einsatz.AddTeamAsync(new Team { TeamName = "Luna" });
        mitHandy.StartTimer();
        ohneHandy.StartTimer();
        mitHandy.ElapsedTime = ohneHandy.ElapsedTime = TimeSpan.FromMinutes(20);
        var zeit = new FesteZeit(Jetzt);
        var (dienst, warnungen) = Ueberwachung(einsatz, zeit);

        dienst.VermerkeSignal(mitHandy.TeamId, Jetzt.AddMinutes(-2));
        dienst.PruefeFunkstille(Jetzt, TimeSpan.FromMinutes(3));
        Assert.Empty(warnungen); // 2 Min. still: noch im Zeitfenster

        dienst.PruefeFunkstille(Jetzt.AddMinutes(2), TimeSpan.FromMinutes(3));
        var warnung = Assert.Single(warnungen); // 4 Min. still – „Luna“ nutzt kein Handy und bleibt ruhig
        Assert.Equal(WarningRuleDefinition.Sources.PhoneNoSignal, warnung.Source);
        Assert.Contains("Rex", warnung.Message);
        Assert.Contains("Traccar gestartet", warnung.Message);
        mitHandy.StopTimer();
        ohneHandy.StopTimer();
    }

    [Fact]
    public async Task Funkstille_NeueSucheZaehltAbStart_NichtAbLetzterSuche()
    {
        var einsatz = new EinsatzService();
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });
        var (dienst, warnungen) = Ueberwachung(einsatz, new FesteZeit(Jetzt));
        dienst.VermerkeSignal(team.TeamId, Jetzt.AddHours(-2)); // Handy sendete bei der vorigen Suche

        team.StartTimer();
        team.ElapsedTime = TimeSpan.FromMinutes(1); // neue Suche laeuft erst seit 1 Minute
        dienst.PruefeFunkstille(Jetzt, TimeSpan.FromMinutes(3));

        Assert.Empty(warnungen);
        team.StopTimer();
    }

    [Fact]
    public async Task HandyAkku_UnterZwanzigProzent_WarntWaehrendDerSuche()
    {
        var einsatz = new EinsatzService();
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });
        team.StartTimer();
        var (dienst, warnungen) = Ueberwachung(einsatz, new FesteZeit(Jetzt));
        await dienst.StartAsync(CancellationToken.None);
        try
        {
            await einsatz.UpdateTeamPhoneLocationAsync(team.TeamId, 51.0, 7.0, batteryPercent: 55);
            await einsatz.UpdateTeamPhoneLocationAsync(team.TeamId, 51.0, 7.0, batteryPercent: 15);

            var warnung = Assert.Single(warnungen);
            Assert.Equal(WarningRuleDefinition.Sources.PhoneLowBattery, warnung.Source);
            Assert.Contains("15 %", warnung.Message);
            Assert.Equal(15, einsatz.PhoneLocations[team.TeamId].BatteryPercent);
        }
        finally
        {
            await dienst.StopAsync(CancellationToken.None);
            team.StopTimer();
        }
    }

    [Fact]
    public void Traccar_AkkuAusFormularUndJson()
    {
        var form = new Dictionary<string, string> { ["id"] = "x", ["lat"] = "51", ["lon"] = "7", ["batt"] = "81.4" };
        Assert.Equal(81, TraccarProtocol.Parse(k => form.GetValueOrDefault(k))!.BatteryPercent);

        var json = """{ "device_id": "x", "location": { "coords": { "latitude": 51, "longitude": 7 }, "battery": { "level": 0.19, "is_charging": false } } }""";
        Assert.Equal(19, TraccarProtocol.ParseJson(json)!.BatteryPercent);
    }

    [Theory]
    [InlineData("https://dein-einsatz.mywire.org", null, "dein-einsatz.mywire.org")]
    [InlineData("dein-einsatz.mywire.org/", null, "dein-einsatz.mywire.org")]
    [InlineData("", "https://andere.example:8443/team", "andere.example")]
    [InlineData("http://nur-http.example", null, null)]
    [InlineData(null, null, null)]
    public void Systemzustand_OeffentlicherHost(string? mobile, string? oeffentlich, string? erwartet) =>
        Assert.Equal(erwartet, SystemZustandService.OeffentlicherHost(mobile, oeffentlich));

    [Fact]
    public void Systemzustand_ZertifikatKritisch_AbgelaufenUngueltigOderBaldFaellig()
    {
        var jetzt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(new ZertifikatZustand("x", jetzt.AddDays(60), null).Kritisch(jetzt));
        Assert.True(new ZertifikatZustand("x", jetzt.AddDays(10), null).Kritisch(jetzt));
        Assert.True(new ZertifikatZustand("x", jetzt.AddDays(-89), "Zertifikat ist abgelaufen").Kritisch(jetzt));
        Assert.Contains("abgelaufen", SystemZustandService.ZertifikatText(new ZertifikatZustand("x", jetzt.AddDays(-89), null), jetzt));
    }

    [Fact]
    public void Leitungsplatz_NameAusVpnGeraetenAnhandDerAdresse()
    {
        var ordner = Path.Combine(Path.GetTempPath(), "eu-geraete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ordner);
        try
        {
            var store = new VpnDeviceStore(ordner);
            store.Add(new VpnDevice { Name = "Tablet EL", Ip = "10.10.0.5", PublicKey = "k1" });
            var namen = new LeitungsGeraetNamen(store);

            Assert.Equal("Tablet EL", namen.NameFuer(IPAddress.Parse("10.10.0.5")));
            Assert.Equal("Tablet EL", namen.NameFuer(IPAddress.Parse("10.10.0.5").MapToIPv6()));
            Assert.Null(namen.NameFuer(IPAddress.Parse("10.10.0.9")));
            Assert.Equal("Einsatzleitung · Tablet EL", LeitungsGeraetNamen.Absender("Tablet EL"));
            Assert.Equal("Einsatzleitung", LeitungsGeraetNamen.Absender(null));
        }
        finally
        {
            Directory.Delete(ordner, true);
        }
    }

    [Fact]
    public void TraccarTest_KennungGiltEineStunde_UndNurGenauDiese()
    {
        var test = new TraccarTestService();
        var jetzt = DateTime.UtcNow;
        var kennung = test.NeueKennung(jetzt);

        Assert.StartsWith(TraccarTestService.Praefix, kennung);
        Assert.False(test.TryVermerke("test-falsch", 51, 7, null, null, "JSON", jetzt));
        Assert.True(test.TryVermerke(kennung, 51, 7, 5, 80, "JSON", jetzt));
        Assert.Equal(80, test.LetzterEmpfang!.BatteryPercent);
        Assert.False(test.TryVermerke(kennung, 51, 7, 5, 80, "JSON", jetzt.AddHours(2)));
        Assert.Contains("id=" + kennung, TraccarTestService.EinrichtungsLink("https://x.example/api/team-mobile/traccar", kennung));
    }
}

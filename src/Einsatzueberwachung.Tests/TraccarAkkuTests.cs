using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Validators;
using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>Traccar auf den Team-Handys sparsam einrichten (#110 Teil 1)</summary>
public class TraccarAkkuTests
{
    private static Dictionary<string, string> Parameter(string link) =>
        link[(link.IndexOf('?') + 1)..].Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    [Fact]
    public void Standard_IstSparsam()
    {
        var p = Parameter(TraccarProtocol.EinrichtungsLink("https://x.example/api/team-mobile/traccar", "tok", new AppSettings()));

        Assert.Equal("https://x.example/api/team-mobile/traccar", p["url"]);
        Assert.Equal("tok", p["id"]);
        Assert.Equal("high", p["accuracy"]);
        Assert.Equal("20", p["interval"]);
        Assert.Equal("10", p["distance"]);
        Assert.Equal("false", p["wakelock"]);
        Assert.Equal("true", p["stop_detection"]);
        Assert.Equal("true", p["buffer"]); // Funkloch-Puffer bleibt
    }

    [Fact]
    public void WerteAusDenEinstellungen_UnsinnigeWerteWerdenBegrenzt()
    {
        var einstellungen = new AppSettings
        {
            TraccarGenauigkeit = "irgendwas",
            TraccarIntervallSekunden = 1,
            TraccarMindestabstandMeter = 25,
            TraccarWachhalten = true,
            TraccarStillstandErkennen = false
        };

        var p = Parameter(TraccarProtocol.EinrichtungsLink("https://x", "t", einstellungen));

        Assert.Equal("high", p["accuracy"]);
        Assert.Equal("5", p["interval"]);
        Assert.Equal("25", p["distance"]);
        Assert.Equal("true", p["wakelock"]);
        Assert.Equal("false", p["stop_detection"]);
        Assert.False(new AppSettingsValidator().Validate(einstellungen).IsValid);
    }

    [Fact]
    public void TraccarTest_NutztDieselbenWerteWieDieTeamSeite()
    {
        var einstellungen = new AppSettings { TraccarIntervallSekunden = 30 };

        Assert.Equal(
            TraccarProtocol.EinrichtungsLink("https://x", "k", einstellungen),
            TraccarTestService.EinrichtungsLink("https://x", "k", einstellungen));
    }
}

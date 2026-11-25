using System.Text.Json;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Tests;

/// <summary>Schalter „Tastenkürzel ausschalten“ (pro Browser, alle Seiten)</summary>
public class TastenkuerzelAusTests
{
    [Fact]
    public void AlteEinstellungenOhneSchalter_KuerzelBleibenAn()
    {
        // localStorage aus der Zeit vor dem Schalter
        var alt = """{"Shortcuts":{"NavHome":"ctrl+h","NavMonitor":"ctrl+n"}}""";
        var prefs = JsonSerializer.Deserialize<BrowserPreferences>(alt)!;
        Assert.False(prefs.Shortcuts.Ausgeschaltet);
        Assert.Equal("ctrl+n", prefs.Shortcuts.NavMonitor);
    }

    [Fact]
    public void SchalterBleibtBeimSpeichernErhalten()
    {
        var prefs = new BrowserPreferences();
        prefs.Shortcuts.Ausgeschaltet = true;
        var wieder = JsonSerializer.Deserialize<BrowserPreferences>(JsonSerializer.Serialize(prefs))!;
        Assert.True(wieder.Shortcuts.Ausgeschaltet);
        Assert.Equal("ctrl+h", wieder.Shortcuts.NavHome); // Belegung bleibt
    }
}

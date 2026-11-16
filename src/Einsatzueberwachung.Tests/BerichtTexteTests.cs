using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>Texte im PDF-Bericht: Dauer, Melder, Notizen (Befunde aus dem Bericht D-38811406)</summary>
public class BerichtTexteTests
{
    [Theory]
    [InlineData(0, 0, 45, "45 s")]
    [InlineData(0, 4, 43, "4 min 43 s")]
    [InlineData(0, 30, 0, "30 min")]
    [InlineData(1, 5, 9, "1 h 05 min")]
    [InlineData(26, 0, 0, "26 h 00 min")]
    public void Dauer_EineSchreibweise(int h, int m, int s, string erwartet) =>
        Assert.Equal(erwartet, PdfExportService.FormatTimeSpan(new TimeSpan(h, m, s)));

    [Theory]
    [InlineData("Polizei Dortmund", "Polizei Dortmund")]
    [InlineData("16.11.2026 19:07", null)]
    [InlineData("19:07", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Melder_UhrzeitWirdNichtAlsMelderGezeigt(string? alarmiert, string? erwartet) =>
        Assert.Equal(erwartet, PdfExportService.Melder(alarmiert));

    [Fact]
    public void NotizText_OhneDoppelteZeitUndOhneSymbole()
    {
        var zeit = new DateTime(2026, 11, 16, 19, 31, 0);
        Assert.Equal("ELW Ankunft Einsatzstelle", PdfExportService.NotizText("[19:31] ELW Ankunft Einsatzstelle", zeit));
        // andere Uhrzeit im Text bleibt stehen
        Assert.Equal("[19:20] ELW Ankunft", PdfExportService.NotizText("[19:20] ELW Ankunft", zeit));
        Assert.Equal("Koordinaten-Marker \"P1\" gesetzt: 51.509116° / 7.084669°",
            PdfExportService.NotizText("📍 Koordinaten-Marker \"P1\" gesetzt: 51.509116° / 7.084669°", zeit));
        Assert.Equal("Gefunden – lebend", PdfExportService.NotizText("✅ Gefunden – lebend", zeit));
    }
}

/// <summary>Karte der Flächenabdeckung: ELW nur in den Ausschnitt, wenn er nahe am Suchgebiet steht</summary>
public class AbdeckungsKarteTests
{
    // Suchgebiet Stadtgarten Gelsenkirchen (etwa 300 × 250 m)
    private const double MinLat = 51.5080, MaxLat = 51.5105, MinLon = 7.0820, MaxLon = 7.0865;

    [Fact]
    public void ElwNebenDemGebiet_ImAusschnitt() =>
        Assert.True(Einsatzueberwachung.Server.Services.OsmStaticMapRenderer.ElwImKartenausschnitt(MinLat, MaxLat, MinLon, MaxLon, (51.5070, 7.0800)));

    [Fact]
    public void ElwInDortmund_NichtImAusschnitt() =>
        Assert.False(Einsatzueberwachung.Server.Services.OsmStaticMapRenderer.ElwImKartenausschnitt(MinLat, MaxLat, MinLon, MaxLon, (51.4990, 7.5600)));
}

using System.Text.Json;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>Suchgebiet aus Koordinatenliste (#96, 1.11.7)</summary>
public class KoordinatenListeTests
{
    // Beispiel aus dem Issue (Harz), letzter Punkt schliesst nicht exakt
    private const string Beispiel =
        "51.70913193,10.53388506 51.70927299,10.53274747 51.70913214,10.53202227 51.70969016,10.53023089 51.70954640,10.52947719 " +
        "51.70900894,10.52893203 51.70858594,10.52930653 51.70816876,10.53011723 51.70779293,10.53216007 51.70913504,10.53380929";

    [Fact]
    public void BeispielAusDemIssue_WirdAlsBreiteLaengeErkannt()
    {
        var e = KoordinatenListe.Einlesen(Beispiel, KoordinatenFormat.Automatisch, out var fehler);

        Assert.Null(fehler);
        Assert.Equal(KoordinatenFormat.BreiteLaenge, e!.ErkanntesFormat);
        Assert.Equal(10, e.Punkte.Count);
        Assert.Equal((51.70913193, 10.53388506), e.Punkte[0]);
    }

    [Fact]
    public void LaengeBreite_UndZeilenweiseMitSemikolon()
    {
        var text = "10.53388506, 51.70913193;\n10.53274747, 51.70927299;\n10.53202227, 51.70913214\n10.53388506,51.70913193";

        var e = KoordinatenListe.Einlesen(text, KoordinatenFormat.Automatisch, out _)!;

        Assert.Equal(KoordinatenFormat.LaengeBreite, e.ErkanntesFormat);
        Assert.Equal(3, e.Punkte.Count); // geschlossener Ring: Endpunkt = Startpunkt entfaellt
        Assert.Equal((51.70913193, 10.53388506), e.Punkte[0]);
    }

    [Fact]
    public void Utm_HinUndZurueck()
    {
        var original = KoordinatenListe.Einlesen(Beispiel, KoordinatenFormat.BreiteLaenge, out _)!.Punkte;
        var utm = KoordinatenListe.Ausgeben(original, KoordinatenFormat.Utm);
        Assert.StartsWith("32U ", utm);

        var zurueck = KoordinatenListe.Einlesen(utm, KoordinatenFormat.Automatisch, out var fehler)!;

        Assert.Null(fehler);
        Assert.Equal(KoordinatenFormat.Utm, zurueck.ErkanntesFormat);
        Assert.Equal(original.Count, zurueck.Punkte.Count);
        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Lat, zurueck.Punkte[i].Lat, 4); // gerundet auf 1 m
            Assert.Equal(original[i].Lon, zurueck.Punkte[i].Lon, 4);
        }
    }

    [Theory]
    [InlineData(KoordinatenFormat.BreiteLaenge)]
    [InlineData(KoordinatenFormat.LaengeBreite)]
    public void Export_IstWiederEinlesbar(KoordinatenFormat format)
    {
        var punkte = KoordinatenListe.Einlesen(Beispiel, KoordinatenFormat.BreiteLaenge, out _)!.Punkte;

        var text = KoordinatenListe.Ausgeben(punkte, format);
        var zurueck = KoordinatenListe.Einlesen(text, format, out _)!.Punkte;

        Assert.Equal(punkte, zurueck);
    }

    [Theory]
    [InlineData("", "einfügen")]
    [InlineData("51.7,10.5 51.8,10.6", "mindestens 3")]
    [InlineData("51.7,10.5 51.8", "Ungerade")]
    [InlineData("95.1,10.5 51.8,10.6 51.9,10.7", "Ungültige")]
    public void FehlerhafteListen_MitVerstaendlicherMeldung(string text, string erwartet)
    {
        Assert.Null(KoordinatenListe.Einlesen(text, KoordinatenFormat.BreiteLaenge, out var fehler));
        Assert.Contains(erwartet, fehler);
    }

    [Fact]
    public void GeoJson_WieVonDerKarte()
    {
        var punkte = KoordinatenListe.Einlesen(Beispiel, KoordinatenFormat.Automatisch, out _)!.Punkte;

        using var doc = JsonDocument.Parse(KoordinatenListe.AlsGeoJson(punkte));
        var ring = doc.RootElement.GetProperty("geometry").GetProperty("coordinates")[0];

        Assert.Equal("Polygon", doc.RootElement.GetProperty("geometry").GetProperty("type").GetString());
        Assert.Equal(11, ring.GetArrayLength()); // geschlossen
        Assert.Equal(10.53388506, ring[0][0].GetDouble()); // [Länge, Breite]
    }
}

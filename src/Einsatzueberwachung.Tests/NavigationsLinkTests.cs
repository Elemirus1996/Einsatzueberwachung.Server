using System.Globalization;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>QR-Code zur Navigation an einem Kartenpunkt (Wunsch 31.10.2026, Variante A)</summary>
public class NavigationsLinkTests
{
    [Fact]
    public void Geo_MitBezeichnung()
    {
        Assert.Equal("geo:49.318800,8.431200?q=49.318800,8.431200(Fundstelle%20Nord)",
            NavigationsLink.Geo(49.3188, 8.4312, "Fundstelle Nord"));
    }

    [Fact]
    public void Geo_OhneBezeichnung_UndKlammernEntfernt()
    {
        Assert.Equal("geo:49.318800,8.431200?q=49.318800,8.431200", NavigationsLink.Geo(49.3188, 8.4312, "  "));
        Assert.Equal("geo:49.318800,8.431200?q=49.318800,8.431200(Treffpunkt%20%20B)",
            NavigationsLink.Geo(49.3188, 8.4312, "Treffpunkt (B)"));
    }

    [Fact]
    public void AppleKarten()
    {
        Assert.Equal("https://maps.apple.com/?ll=49.318800,8.431200&q=Br%C3%BCcke%20%26%20Weg",
            NavigationsLink.AppleKarten(49.3188, 8.4312, "Brücke & Weg"));
        Assert.Equal("https://maps.apple.com/?ll=-33.900000,-151.200000&q=-33.900000,-151.200000",
            NavigationsLink.AppleKarten(-33.9, -151.2));
    }

    [Fact]
    public void Dezimalpunkt_AuchBeiDeutscherKultur()
    {
        var vorher = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.StartsWith("geo:49.318800,8.431200", NavigationsLink.Geo(49.3188, 8.4312));
        }
        finally
        {
            CultureInfo.CurrentCulture = vorher;
        }
    }

    [Fact]
    public void LangeBezeichnung_WirdGekuerzt()
    {
        var link = NavigationsLink.Geo(49.3188, 8.4312, new string('x', 200));
        Assert.Contains("(" + new string('x', 60) + ")", link);
    }

    [Fact]
    public void UngueltigeKoordinaten()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NavigationsLink.Geo(91, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => NavigationsLink.AppleKarten(49, double.NaN));
    }

    [Fact]
    public void QrBild_IstPngDataUrl()
    {
        Assert.StartsWith("data:image/png;base64,", QrBild.DataUrl(NavigationsLink.Geo(49.3188, 8.4312, "Test")));
    }

    [Fact]
    public void GoogleMapsUndWaze()
    {
        Assert.Equal("comgooglemaps://?daddr=49.318800,8.431200&directionsmode=walking", NavigationsLink.GoogleMaps(49.3188, 8.4312));
        Assert.Equal("waze://?ll=49.318800,8.431200&navigate=yes", NavigationsLink.Waze(49.3188, 8.4312));
        Assert.Throws<ArgumentOutOfRangeException>(() => NavigationsLink.Waze(91, 8));
    }
}

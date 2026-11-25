using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

public class LuftbildLaenderTests
{
    [Fact]
    public void Liste_EnthaeltVierzehnLaenderMitVollstaendigenAngaben()
    {
        var alle = LuftbildLaender.Alle;
        Assert.Equal(14, alle.Count);
        Assert.Equal(alle.Count, alle.Select(q => q.Land).Distinct().Count());
        Assert.All(alle, q =>
        {
            Assert.StartsWith("https://", q.Url);
            Assert.False(string.IsNullOrWhiteSpace(q.Ebenen));
            Assert.False(string.IsNullOrWhiteSpace(q.Quelle));
            Assert.Contains(q.Version, new[] { "1.1.1", "1.3.0" });
            Assert.Equal(4, q.Box.Length);
            Assert.True(q.Box[0] < q.Box[2] && q.Box[1] < q.Box[3], q.Land);
            Assert.InRange(q.Box[0], 5.5, 15.5);
            Assert.InRange(q.Box[1], 47, 55.5);
        });
    }

    [Fact]
    public void Fuer_LiefertAnLandesgrenzeMehrereLaender()
    {
        // Ausschnitt an der Grenze Hessen / Bayern / Thüringen (Rhön)
        var laender = LuftbildLaender.Fuer(9.9, 50.4, 10.1, 50.6).Select(q => q.Land).ToList();
        Assert.Contains("HE", laender);
        Assert.Contains("BY", laender);
        Assert.Contains("TH", laender);
        Assert.Empty(LuftbildLaender.Fuer(2.0, 45.0, 2.1, 45.1));
    }

    [Fact]
    public void KachelUrl_BautGetMapMitWebMercator()
    {
        var by = LuftbildLaender.Alle.Single(q => q.Land == "BY");
        var url = by.KachelUrl(1000.5, 2000, 3000, 4000.25, 256);
        Assert.StartsWith("https://geoservices.bayern.de/od/wms/dop/v1/dop20?SERVICE=WMS&REQUEST=GetMap&VERSION=1.1.1", url);
        Assert.Contains("&SRS=EPSG%3A3857&BBOX=1000.5,2000,3000,4000.25&WIDTH=256&HEIGHT=256", url);
        Assert.Contains("TRANSPARENT=TRUE", url);

        var hb = LuftbildLaender.Alle.Single(q => q.Land == "HB");
        Assert.Contains("&CRS=EPSG%3A3857", hb.KachelUrl(0, 0, 1, 1, 256));
        Assert.Contains("LAYERS=DOP20_2023_HB%2CDOP20_2023_BHV", hb.KachelUrl(0, 0, 1, 1, 256));
    }

    [Fact]
    public void Quellenvermerk_SetztJahrEinUndNenntJedesLand()
    {
        var quellen = LuftbildLaender.Alle.Where(q => q.Land is "NI" or "BB").ToList();
        var text = LuftbildLaender.Quellenvermerk(quellen, 2026);
        Assert.StartsWith("Luftbild: ", text);
        Assert.Contains("LGLN (2026), CC BY 4.0", text);
        Assert.Contains("GeoBasis-DE/LGB", text);
        Assert.DoesNotContain("{jahr}", text);
        Assert.Equal("Luftbild: keine Daten der Länder", LuftbildLaender.Quellenvermerk([], 2026));
    }

    [Fact]
    public void Kachelrechnung_StimmtFuerBekannteKachel()
    {
        // Kachel 0/0/0 umfasst die ganze Welt
        var (w, s, o, n) = OsmStaticMapRenderer.KachelGrad(0, 0, 0);
        Assert.Equal(-180, w, 6);
        Assert.Equal(180, o, 6);
        Assert.Equal(85.0511, n, 3);
        Assert.Equal(-85.0511, s, 3);
        var (minX, minY, maxX, maxY) = OsmStaticMapRenderer.KachelMercator(1, 1, 1);
        Assert.Equal(0, minX, 3);
        Assert.Equal(-20037508.343, minY, 3);
        Assert.Equal(20037508.343, maxX, 3);
        Assert.Equal(0, maxY, 3);
    }

    [Fact]
    public void Bericht_BietetLuftbildAn()
    {
        Assert.Contains(PdfBerichtOptionen.Kartenarten, k => k.Art == MapTileType.Luftbild);
    }
}

public class EsriSchluesselTests
{
    private static ISettingsService Einstellungen(string schluessel) =>
        EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) => m.Name switch
        {
            nameof(ISettingsService.GetAppSettingsAsync) => Task.FromResult(new AppSettings { EsriApiKey = schluessel }),
            _ => null
        }).Proxy;

    private sealed class KeinNetz : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public void Kartenarten_EsriNurMitSchluessel()
    {
        Assert.DoesNotContain(PdfBerichtOptionen.KartenartenFuer(false), k => k.Art == MapTileType.Satellite);
        Assert.Contains(PdfBerichtOptionen.KartenartenFuer(true), k => k.Art == MapTileType.Satellite);
        Assert.Contains(PdfBerichtOptionen.KartenartenFuer(false), k => k.Art == MapTileType.Luftbild);
    }

    [Fact]
    public void Adresse_SetztSchluesselKodiertEin()
    {
        Assert.False(EsriKacheln.Vorhanden("  "));
        var url = EsriKacheln.Adresse(" ab+c/d ");
        Assert.StartsWith("https://ibasemaps-api.arcgis.com/arcgis/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}?token=", url);
        Assert.EndsWith("token=ab%2Bc%2Fd", url);
    }

    [Fact]
    public async Task Renderer_NutztEsriNurMitSchluessel_SonstLuftbild()
    {
        var mit = new OsmStaticMapRenderer(new KeinNetz(), Microsoft.Extensions.Logging.Abstractions.NullLogger<OsmStaticMapRenderer>.Instance, Einstellungen("geheim"));
        var config = await mit.KachelConfigAsync(MapTileType.Satellite);
        Assert.False(config.Luftbild);
        Assert.Equal(EsriKacheln.Quelle, config.Attribution);
        Assert.EndsWith("token=geheim", Assert.Single(config.UrlTemplates));

        var ohne = new OsmStaticMapRenderer(new KeinNetz(), Microsoft.Extensions.Logging.Abstractions.NullLogger<OsmStaticMapRenderer>.Instance, Einstellungen(""));
        Assert.True((await ohne.KachelConfigAsync(MapTileType.Satellite)).Luftbild);
        Assert.False((await ohne.KachelConfigAsync(MapTileType.Streets)).Luftbild);
    }
}

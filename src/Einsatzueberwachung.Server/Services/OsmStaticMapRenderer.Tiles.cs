using Einsatzueberwachung.Domain.Models.Enums;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Einsatzueberwachung.Server.Services;

public sealed partial class OsmStaticMapRenderer
{
    internal record SearchAreaTileConfig(string[] UrlTemplates, int PixelSize, string Attribution, bool Luftbild = false);

    private static SearchAreaTileConfig GetSearchAreaTileConfig(MapTileType tileType) => tileType switch
    {
        // Esri nur mit Schlüssel der Staffel: Platzhalter {token} setzt KachelConfigAsync ein
        MapTileType.Satellite => new([EsriKacheln.Vorlage], 256, EsriKacheln.Quelle),

        // Amtliche Luftbilder der Länder: je Kachel alle Länder, die sie berühren, übereinander (Issue 210)
        MapTileType.Luftbild => new([], 256, "", Luftbild: true),

        MapTileType.Topographic => new(
            [
                "https://a.tile.opentopomap.org/{z}/{x}/{y}.png",
                "https://b.tile.opentopomap.org/{z}/{x}/{y}.png",
                "https://c.tile.opentopomap.org/{z}/{x}/{y}.png"
            ],
            256,
            "© OpenStreetMap contributors | © OpenTopoMap (CC-BY-SA)"),

        _ => new(
            ["https://tile.openstreetmap.org/{z}/{x}/{y}.png"],
            256,
            OsmAttribution)
    };

    /// <summary>
    /// Kachelquelle für eine Kartenart. Esri nur mit Schlüssel der Staffel; ohne Schlüssel die Luftbilder der Länder.
    /// </summary>
    internal async Task<SearchAreaTileConfig> KachelConfigAsync(MapTileType tileType)
    {
        var config = GetSearchAreaTileConfig(tileType);
        if (tileType != MapTileType.Satellite) return config;

        var schluessel = _einstellungen is null ? null : (await _einstellungen.GetAppSettingsAsync())?.EsriApiKey;
        return EsriKacheln.Vorhanden(schluessel)
            ? config with { UrlTemplates = [EsriKacheln.Adresse(schluessel!)] }
            : GetSearchAreaTileConfig(MapTileType.Luftbild);
    }

    // OpenStreetMap: Nutzungsregeln eingehalten (eigener User-Agent, hoechstens 2 Downloads gleichzeitig,
    // nur die Kacheln des Berichtsausschnitts)
    private const string OsmAttribution = "© OpenStreetMap-Mitwirkende";

    private static readonly string[] TileServers =
    [
        "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
    ];

    private async Task<Dictionary<(int x, int y), byte[]?>> DownloadTilesAsync(
        int minX, int maxX, int minY, int maxY, int zoom, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(2);
        var tasks = new List<(int x, int y, Task<byte[]?> task)>();

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                var cx = x; var cy = y;
                tasks.Add((cx, cy, ThrottledDownloadAsync(semaphore, cx, cy, zoom, cancellationToken)));
            }

        await Task.WhenAll(tasks.Select(t => t.task));

        return tasks.ToDictionary(t => (t.x, t.y), t => t.task.Result);
    }

    private async Task<byte[]?> ThrottledDownloadAsync(SemaphoreSlim semaphore, int x, int y, int zoom, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try { return await DownloadTileAsync(x, y, zoom, TileServers, cancellationToken); }
        finally { semaphore.Release(); }
    }

    private async Task<byte[]?> DownloadTileAsync(int x, int y, int zoom, string[] urlTemplates, CancellationToken cancellationToken)
    {
        foreach (var template in urlTemplates)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            using var perTileCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            perTileCts.CancelAfter(TileTimeout);
            try
            {
                var url = template.Replace("{z}", zoom.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
                var response = await _httpClient.GetAsync(url, perTileCts.Token);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsByteArrayAsync(perTileCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Tile {Zoom}/{X}/{Y} konnte nicht geladen werden", zoom, x, y);
            }
        }
        return null;
    }

    private async Task<Dictionary<(int x, int y), byte[]?>> DownloadTilesWithConfigAsync(
        int minX, int maxX, int minY, int maxY, int zoom, SearchAreaTileConfig config, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(2);
        var tasks = new List<(int x, int y, Task<byte[]?> task)>();

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                var cx = x; var cy = y;
                tasks.Add((cx, cy, ThrottledDownloadWithConfigAsync(semaphore, cx, cy, zoom, config, cancellationToken)));
            }

        await Task.WhenAll(tasks.Select(t => t.task));

        return tasks.ToDictionary(t => (t.x, t.y), t => t.task.Result);
    }

    private async Task<byte[]?> ThrottledDownloadWithConfigAsync(SemaphoreSlim semaphore, int x, int y, int zoom, SearchAreaTileConfig config, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return config.Luftbild
                ? await DownloadLuftbildKachelAsync(x, y, zoom, cancellationToken)
                : await DownloadTileAsync(x, y, zoom, config.UrlTemplates, cancellationToken);
        }
        finally { semaphore.Release(); }
    }

    /// <summary>Eine Kachel aus den Luftbildern aller Länder, die sie berühren (an Grenzen mehrere übereinander).</summary>
    private async Task<byte[]?> DownloadLuftbildKachelAsync(int x, int y, int zoom, CancellationToken cancellationToken)
    {
        var (west, sued, ost, nord) = KachelGrad(x, y, zoom);
        var quellen = LuftbildLaender.Fuer(west, sued, ost, nord);
        if (quellen.Count == 0) return null;

        var (minX, minY, maxX, maxY) = KachelMercator(x, y, zoom);
        var bilder = new List<byte[]>();
        foreach (var quelle in quellen)
        {
            var bild = await DownloadTileAsync(x, y, zoom, [quelle.KachelUrl(minX, minY, maxX, maxY, TileSize)], cancellationToken);
            if (bild != null) bilder.Add(bild);
        }
        if (bilder.Count <= 1) return bilder.FirstOrDefault();

        using var bitmap = new SKBitmap(TileSize, TileSize);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            foreach (var bild in bilder)
            {
                using var teil = SKBitmap.Decode(bild);
                if (teil != null) canvas.DrawBitmap(teil, new SKRect(0, 0, TileSize, TileSize));
            }
        }
        return EncodeAsPng(bitmap);
    }

    /// <summary>Quellenvermerk passend zum Ausschnitt: bei Luftbildern nur die Länder, die darin vorkommen.</summary>
    private static string KartenQuelle(SearchAreaTileConfig config, int minTileX, int maxTileX, int minTileY, int maxTileY, int zoom)
    {
        if (!config.Luftbild) return config.Attribution;
        var (west, _, _, nord) = KachelGrad(minTileX, minTileY, zoom);
        var (_, sued, ost, _) = KachelGrad(maxTileX, maxTileY, zoom);
        return LuftbildLaender.Quellenvermerk(LuftbildLaender.Fuer(west, sued, ost, nord), DateTime.UtcNow.Year);
    }

    internal static (double West, double Sued, double Ost, double Nord) KachelGrad(int x, int y, int zoom)
    {
        double n = Math.Pow(2, zoom);
        static double Breite(double yy, double nn) => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * yy / nn))) * 180 / Math.PI;
        return (x / n * 360 - 180, Breite(y + 1, n), (x + 1) / n * 360 - 180, Breite(y, n));
    }

    internal static (double MinX, double MinY, double MaxX, double MaxY) KachelMercator(int x, int y, int zoom)
    {
        const double halb = 20037508.342789244;
        var groesse = 2 * halb / Math.Pow(2, zoom);
        return (-halb + x * groesse, halb - (y + 1) * groesse, -halb + (x + 1) * groesse, halb - y * groesse);
    }

    private static (SKBitmap bitmap, int w, int h) AssembleTileMosaic(
        Dictionary<(int x, int y), byte[]?> tiles,
        int minTileX, int minTileY, int maxTileX, int maxTileY, int pixelSize)
    {
        var tileCountX = maxTileX - minTileX + 1;
        var tileCountY = maxTileY - minTileY + 1;
        var fullW = tileCountX * pixelSize;
        var fullH = tileCountY * pixelSize;

        var bitmap = new SKBitmap(fullW, fullH);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(228, 228, 228));

        foreach (var ((tx, ty), tileData) in tiles)
        {
            if (tileData == null) continue;
            using var tileBitmap = SKBitmap.Decode(tileData);
            if (tileBitmap == null) continue;
            canvas.DrawBitmap(tileBitmap, (tx - minTileX) * pixelSize, (ty - minTileY) * pixelSize);
        }

        return (bitmap, fullW, fullH);
    }

    private static void AdjustCropToAspect(
        ref double left, ref double top, ref double right, ref double bottom,
        int targetWidth, int targetHeight)
    {
        var cropW = right - left;
        var cropH = bottom - top;
        var targetAspect = (double)targetWidth / targetHeight;
        var cropAspect = cropW / cropH;

        if (cropAspect < targetAspect)
        {
            var delta = (cropH * targetAspect - cropW) / 2.0;
            left -= delta; right += delta;
        }
        else
        {
            var delta = (cropW / targetAspect - cropH) / 2.0;
            top -= delta; bottom += delta;
        }
    }

    private static byte[] EncodeAsPng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}

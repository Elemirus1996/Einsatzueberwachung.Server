using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Rendert statische Kartenbilder aus Carto-/OSM-Tiles mit SkiaSharp.
/// Zeichnet GPS-Tracks und Suchgebiete als Overlay.
/// </summary>
public sealed class OsmStaticMapRenderer : IStaticMapRenderer, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OsmStaticMapRenderer> _logger;

    // Carto @2x Tiles sind 512×512px
    private const int TileSize = 512;

    public OsmStaticMapRenderer(IHttpClientFactory httpClientFactory, ILogger<OsmStaticMapRenderer> logger)
    {
        _httpClient = httpClientFactory.CreateClient("OsmTiles");
        _logger = logger;
    }

    public async Task<byte[]?> RenderTrackMapAsync(
        List<TrackPoint> trackPoints,
        List<(double Latitude, double Longitude)>? searchAreaCoords,
        string trackColor,
        string? areaColor,
        int width = 800,
        int height = 450)
    {
        if (trackPoints.Count < 2) return null;

        try
        {
            // 1. Bounding Box berechnen (Track + Suchgebiet)
            var allLats = trackPoints.Select(p => p.Latitude).ToList();
            var allLons = trackPoints.Select(p => p.Longitude).ToList();
            if (searchAreaCoords is { Count: >= 3 })
            {
                allLats.AddRange(searchAreaCoords.Select(c => c.Latitude));
                allLons.AddRange(searchAreaCoords.Select(c => c.Longitude));
            }

            var minLat = allLats.Min();
            var maxLat = allLats.Max();
            var minLon = allLons.Min();
            var maxLon = allLons.Max();

            // 10% Padding
            var latPad = Math.Max((maxLat - minLat) * 0.10, 0.001);
            var lonPad = Math.Max((maxLon - minLon) * 0.10, 0.001);
            minLat -= latPad; maxLat += latPad;
            minLon -= lonPad; maxLon += lonPad;

            // 2. Optimalen Zoom berechnen
            var zoom = CalculateZoom(minLat, maxLat, minLon, maxLon, width, height);

            // 3. Crop-Bereich in absoluten Tile-Pixeln berechnen und
            //    Seitenverhältnis VOR dem Tile-Download anpassen,
            //    damit genug Kacheln geladen werden (kein weißer Rand).
            var absCropLeft = LonToTileXFloat(minLon, zoom) * TileSize;
            var absCropTop = LatToTileYFloat(maxLat, zoom) * TileSize;
            var absCropRight = LonToTileXFloat(maxLon, zoom) * TileSize;
            var absCropBottom = LatToTileYFloat(minLat, zoom) * TileSize;

            var cropW = absCropRight - absCropLeft;
            var cropH = absCropBottom - absCropTop;
            var targetAspect = (double)width / height;
            var cropAspect = cropW / cropH;

            if (cropAspect < targetAspect)
            {
                // Crop ist zu hoch → breiter machen
                var newW = cropH * targetAspect;
                var delta = (newW - cropW) / 2.0;
                absCropLeft -= delta;
                absCropRight += delta;
            }
            else
            {
                // Crop ist zu breit → höher machen
                var newH = cropW / targetAspect;
                var delta = (newH - cropH) / 2.0;
                absCropTop -= delta;
                absCropBottom += delta;
            }

            // 4. Tile-Bereich aus dem angepassten Crop ableiten
            var minTileX = (int)Math.Floor(absCropLeft / TileSize);
            var maxTileX = (int)Math.Floor(absCropRight / TileSize);
            var minTileY = (int)Math.Floor(absCropTop / TileSize);
            var maxTileY = (int)Math.Floor(absCropBottom / TileSize);

            // 5. Tiles herunterladen
            var tiles = await DownloadTilesAsync(minTileX, maxTileX, minTileY, maxTileY, zoom);

            // 6. Tile-Mosaik zusammensetzen
            var tileCountX = maxTileX - minTileX + 1;
            var tileCountY = maxTileY - minTileY + 1;
            var fullW = tileCountX * TileSize;
            var fullH = tileCountY * TileSize;

            using var fullBitmap = new SKBitmap(fullW, fullH);
            using (var tileCanvas = new SKCanvas(fullBitmap))
            {
                tileCanvas.Clear(new SKColor(228, 228, 228));
                foreach (var ((tx, ty), tileData) in tiles)
                {
                    if (tileData == null) continue;
                    using var tileBitmap = SKBitmap.Decode(tileData);
                    if (tileBitmap == null) continue;
                    var px = (tx - minTileX) * TileSize;
                    var py = (ty - minTileY) * TileSize;
                    tileCanvas.DrawBitmap(tileBitmap, px, py);
                }
            }

            // 7. Auf Zielgröße zuschneiden (Crop relativ zum Mosaik-Ursprung)
            var cropLeft = (float)(absCropLeft - minTileX * TileSize);
            var cropTop = (float)(absCropTop - minTileY * TileSize);
            var cropRight = (float)(absCropRight - minTileX * TileSize);
            var cropBottom = (float)(absCropBottom - minTileY * TileSize);

            var srcRect = new SKRect(cropLeft, cropTop, cropRight, cropBottom);
            var dstRect = new SKRect(0, 0, width, height);

            using var outputBitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(outputBitmap);
            canvas.DrawBitmap(fullBitmap, srcRect, dstRect);

            // Geo → Output-Pixel Umrechnung (direkt in Output-Koordinaten)
            var scaleX = width / (cropRight - cropLeft);
            var scaleY = height / (cropBottom - cropTop);
            float ToX(double lon) =>
                ((float)((LonToTileXFloat(lon, zoom) - minTileX) * TileSize) - cropLeft) * scaleX;
            float ToY(double lat) =>
                ((float)((LatToTileYFloat(lat, zoom) - minTileY) * TileSize) - cropTop) * scaleY;

            // 8. Suchgebiet-Polygon zeichnen (auf Output-Canvas — scharfe Pixel)
            if (searchAreaCoords is { Count: >= 3 })
            {
                var areaPoints = searchAreaCoords
                    .Select(c => new SKPoint(ToX(c.Longitude), ToY(c.Latitude)))
                    .ToArray();

                var parsedAreaColor = ParseColor(areaColor ?? "#3388ff");

                using var areaPath = new SKPath();
                areaPath.MoveTo(areaPoints[0]);
                for (int i = 1; i < areaPoints.Length; i++)
                    areaPath.LineTo(areaPoints[i]);
                areaPath.Close();

                // Halbtransparente Füllung
                using var fillPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = parsedAreaColor.WithAlpha(50),
                    IsAntialias = true
                };
                canvas.DrawPath(areaPath, fillPaint);

                // Gestrichelter Rand
                using var strokePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = parsedAreaColor,
                    StrokeWidth = 2.5f,
                    IsAntialias = true,
                    PathEffect = SKPathEffect.CreateDash(new[] { 12f, 6f }, 0)
                };
                canvas.DrawPath(areaPath, strokePaint);
            }

            // 8. Track-Linie zeichnen (auf Output-Canvas — scharfe Pixel)
            var trackPts = trackPoints
                .Select(p => new SKPoint(ToX(p.Longitude), ToY(p.Latitude)))
                .ToArray();

            if (trackPts.Length >= 2)
            {
                using var trackPath = new SKPath();
                trackPath.MoveTo(trackPts[0]);
                for (int i = 1; i < trackPts.Length; i++)
                    trackPath.LineTo(trackPts[i]);

                // Schatten
                using var shadowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = new SKColor(0, 0, 0, 60),
                    StrokeWidth = 6f,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    IsAntialias = true
                };
                canvas.DrawPath(trackPath, shadowPaint);

                // Track-Linie
                var parsedTrackColor = ParseColor(trackColor);
                using var trackPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = parsedTrackColor,
                    StrokeWidth = 3.5f,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    IsAntialias = true
                };
                canvas.DrawPath(trackPath, trackPaint);

                // Start-Marker (grün)
                DrawMarker(canvas, trackPts[0], new SKColor(40, 167, 69), "Start");

                // Ende-Marker (rot)
                DrawMarker(canvas, trackPts[^1], new SKColor(220, 53, 69), "Ende");
            }

            // 9. Attribution (klein, unten rechts)
            using var attrFont = new SKFont(SKTypeface.Default, 10);
            using var attrPaint = new SKPaint
            {
                Color = new SKColor(80, 80, 80),
                IsAntialias = true
            };
            using var attrBgPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = new SKColor(255, 255, 255, 180)
            };
            const string attribution = "© OpenStreetMap © CARTO";
            attrFont.MeasureText(attribution, out var textBounds);
            var attrX = width - textBounds.Width - 6;
            var attrY = height - 6;
            canvas.DrawRect(attrX - 3, attrY - textBounds.Height - 2, textBounds.Width + 6, textBounds.Height + 4, attrBgPaint);
            canvas.DrawText(attribution, attrX, attrY, SKTextAlign.Left, attrFont, attrPaint);

            // PNG exportieren
            using var image = SKImage.FromBitmap(outputBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Rendern der statischen Karte — Fallback auf SVG");
            return null;
        }
    }

    private static void DrawMarker(SKCanvas canvas, SKPoint center, SKColor color, string label)
    {
        // Weißer Rand
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.White,
            IsAntialias = true
        };
        canvas.DrawCircle(center, 10, borderPaint);

        // Farbiger Kreis
        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color,
            IsAntialias = true
        };
        canvas.DrawCircle(center, 8, fillPaint);

        // Weißes Label-Hintergrund für bessere Lesbarkeit
        using var labelFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 13);
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 200),
            IsAntialias = true
        };
        labelFont.MeasureText(label, out var lblBounds);
        canvas.DrawRoundRect(
            center.X + 13, center.Y - lblBounds.Height / 2 - 2,
            lblBounds.Width + 6, lblBounds.Height + 4,
            3, 3, bgPaint);

        // Label Text
        using var textPaint = new SKPaint
        {
            Color = color,
            IsAntialias = true
        };
        canvas.DrawText(label, center.X + 16, center.Y + lblBounds.Height / 2 - 1,
            SKTextAlign.Left, labelFont, textPaint);
    }

    // ─── Tile Download ──────────────────────────────────────────

    private async Task<Dictionary<(int x, int y), byte[]?>> DownloadTilesAsync(
        int minX, int maxX, int minY, int maxY, int zoom)
    {
        var result = new Dictionary<(int, int), byte[]?>();
        var semaphore = new SemaphoreSlim(2);
        var tasks = new List<(int x, int y, Task<byte[]?> task)>();

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var cx = x; var cy = y;
                tasks.Add((cx, cy, ThrottledDownload(semaphore, cx, cy, zoom)));
            }
        }

        await Task.WhenAll(tasks.Select(t => t.task));

        foreach (var (x, y, task) in tasks)
        {
            result[(x, y)] = task.Result;
        }

        semaphore.Dispose();
        return result;
    }

    private async Task<byte[]?> ThrottledDownload(SemaphoreSlim semaphore, int x, int y, int zoom)
    {
        await semaphore.WaitAsync();
        try
        {
            return await DownloadTileAsync(x, y, zoom);
        }
        finally
        {
            semaphore.Release();
        }
    }

    // Tile-Server mit Fallback-Kette
    private static readonly string[] TileServers =
    [
        "https://basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}@2x.png",
        "https://a.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}@2x.png",
        "https://b.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}@2x.png",
    ];

    private async Task<byte[]?> DownloadTileAsync(int x, int y, int zoom)
    {
        foreach (var template in TileServers)
        {
            try
            {
                var url = template
                    .Replace("{z}", zoom.ToString())
                    .Replace("{x}", x.ToString())
                    .Replace("{y}", y.ToString());
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Tile {Zoom}/{X}/{Y} konnte nicht geladen werden", zoom, x, y);
            }
        }
        return null;
    }

    // ─── Tile-Mathematik (Slippy Map) ───────────────────────────

    private static int LonToTileX(double lon, int zoom)
        => (int)Math.Floor(LonToTileXFloat(lon, zoom));

    private static int LatToTileY(double lat, int zoom)
        => (int)Math.Floor(LatToTileYFloat(lat, zoom));

    private static double LonToTileXFloat(double lon, int zoom)
        => (lon + 180.0) / 360.0 * (1 << zoom);

    private static double LatToTileYFloat(double lat, int zoom)
    {
        var latRad = lat * Math.PI / 180.0;
        return (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << zoom);
    }

    private static int CalculateZoom(double minLat, double maxLat, double minLon, double maxLon, int imgWidth, int imgHeight)
    {
        for (int z = 17; z >= 2; z--)
        {
            var x1 = LonToTileXFloat(minLon, z) * TileSize;
            var x2 = LonToTileXFloat(maxLon, z) * TileSize;
            var y1 = LatToTileYFloat(maxLat, z) * TileSize;
            var y2 = LatToTileYFloat(minLat, z) * TileSize;

            if ((x2 - x1) <= imgWidth * 1.2 && (y2 - y1) <= imgHeight * 1.2)
                return z;
        }
        return 2;
    }

    private static SKColor ParseColor(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 7)
            return new SKColor(255, 68, 68);

        try
        {
            hex = hex.TrimStart('#');
            var r = Convert.ToByte(hex[..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            return new SKColor(r, g, b);
        }
        catch
        {
            return new SKColor(255, 68, 68);
        }
    }

    public void Dispose()
    {
        // HttpClient wird vom Factory verwaltet
    }
}

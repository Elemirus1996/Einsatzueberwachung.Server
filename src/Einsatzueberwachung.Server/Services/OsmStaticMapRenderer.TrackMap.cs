using Einsatzueberwachung.Domain.Models;
using SkiaSharp;

namespace Einsatzueberwachung.Server.Services;

public sealed partial class OsmStaticMapRenderer
{
    public async Task<byte[]?> RenderTrackMapAsync(
        List<TrackPoint> trackPoints,
        List<(double Latitude, double Longitude)>? searchAreaCoords,
        string trackColor,
        string? areaColor,
        int width = 800,
        int height = 450)
    {
        if (trackPoints.Count < 2) return null;

        using var renderCts = new CancellationTokenSource(RenderTimeout);
        var ct = renderCts.Token;

        if (!await _globalRenderLock.WaitAsync(RenderTimeout))
        {
            _logger.LogWarning("Track-Karte: globaler Render-Lock-Timeout, überspringe Karte");
            return null;
        }

        try
        {
            var allLats = trackPoints.Select(p => p.Latitude).ToList();
            var allLons = trackPoints.Select(p => p.Longitude).ToList();
            if (searchAreaCoords is { Count: >= 3 })
            {
                allLats.AddRange(searchAreaCoords.Select(c => c.Latitude));
                allLons.AddRange(searchAreaCoords.Select(c => c.Longitude));
            }

            var minLat = allLats.Min(); var maxLat = allLats.Max();
            var minLon = allLons.Min(); var maxLon = allLons.Max();

            var latPad = Math.Max((maxLat - minLat) * 0.10, 0.001);
            var lonPad = Math.Max((maxLon - minLon) * 0.10, 0.001);
            minLat -= latPad; maxLat += latPad;
            minLon -= lonPad; maxLon += lonPad;

            var zoom = CalculateZoom(minLat, maxLat, minLon, maxLon, width, height);

            var absCropLeft = LonToTileXFloat(minLon, zoom) * TileSize;
            var absCropTop = LatToTileYFloat(maxLat, zoom) * TileSize;
            var absCropRight = LonToTileXFloat(maxLon, zoom) * TileSize;
            var absCropBottom = LatToTileYFloat(minLat, zoom) * TileSize;

            AdjustCropToAspect(ref absCropLeft, ref absCropTop, ref absCropRight, ref absCropBottom, width, height);

            var minTileX = (int)Math.Floor(absCropLeft / TileSize);
            var maxTileX = (int)Math.Floor(absCropRight / TileSize);
            var minTileY = (int)Math.Floor(absCropTop / TileSize);
            var maxTileY = (int)Math.Floor(absCropBottom / TileSize);

            var tiles = await DownloadTilesAsync(minTileX, maxTileX, minTileY, maxTileY, zoom, ct);
            var (fullBitmap, fullW, fullH) = AssembleTileMosaic(tiles, minTileX, minTileY, maxTileX, maxTileY, TileSize);

            using (fullBitmap)
            {
                var cropLeft = (float)(absCropLeft - minTileX * TileSize);
                var cropTop = (float)(absCropTop - minTileY * TileSize);
                var cropRight = (float)(absCropRight - minTileX * TileSize);
                var cropBottom = (float)(absCropBottom - minTileY * TileSize);

                using var outputBitmap = new SKBitmap(width, height);
                using var canvas = new SKCanvas(outputBitmap);
                canvas.DrawBitmap(fullBitmap, new SKRect(cropLeft, cropTop, cropRight, cropBottom), new SKRect(0, 0, width, height));

                var scaleX = width / (cropRight - cropLeft);
                var scaleY = height / (cropBottom - cropTop);
                float ToX(double lon) => ((float)((LonToTileXFloat(lon, zoom) - minTileX) * TileSize) - cropLeft) * scaleX;
                float ToY(double lat) => ((float)((LatToTileYFloat(lat, zoom) - minTileY) * TileSize) - cropTop) * scaleY;

                if (searchAreaCoords is { Count: >= 3 })
                    DrawSearchAreaOverlay(canvas, searchAreaCoords, ToX, ToY, areaColor ?? "#3388ff");

                var trackPts = trackPoints.Select(p => new SKPoint(ToX(p.Longitude), ToY(p.Latitude))).ToArray();
                if (trackPts.Length >= 2)
                {
                    DrawTrackLine(canvas, trackPts, trackColor);
                    DrawMarker(canvas, trackPts[0], new SKColor(40, 167, 69), "Start");
                    DrawMarker(canvas, trackPts[^1], new SKColor(220, 53, 69), "Ende");
                }

                DrawAttribution(canvas, width, height, OsmAttribution);

                return EncodeAsPng(outputBitmap);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Track-Karte: Render-Timeout nach {Sec}s erreicht — Fallback auf SVG", RenderTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Rendern der statischen Karte — Fallback auf SVG");
            return null;
        }
        finally
        {
            _globalRenderLock.Release();
        }
    }

    private static void DrawSearchAreaOverlay(
        SKCanvas canvas,
        List<(double Latitude, double Longitude)> coords,
        Func<double, float> toX,
        Func<double, float> toY,
        string colorHex)
    {
        var areaPoints = coords.Select(c => new SKPoint(toX(c.Longitude), toY(c.Latitude))).ToArray();
        var parsedColor = ParseColor(colorHex);

        using var areaPath = new SKPath();
        areaPath.MoveTo(areaPoints[0]);
        for (int i = 1; i < areaPoints.Length; i++)
            areaPath.LineTo(areaPoints[i]);
        areaPath.Close();

        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = parsedColor.WithAlpha(50), IsAntialias = true };
        canvas.DrawPath(areaPath, fillPaint);

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = parsedColor,
            StrokeWidth = 2.5f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 12f, 6f }, 0)
        };
        canvas.DrawPath(areaPath, strokePaint);
    }

    private static void DrawTrackLine(SKCanvas canvas, SKPoint[] trackPts, string trackColor)
    {
        using var trackPath = new SKPath();
        trackPath.MoveTo(trackPts[0]);
        for (int i = 1; i < trackPts.Length; i++)
            trackPath.LineTo(trackPts[i]);

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

        using var trackPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ParseColor(trackColor),
            StrokeWidth = 3.5f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };
        canvas.DrawPath(trackPath, trackPaint);
    }
}

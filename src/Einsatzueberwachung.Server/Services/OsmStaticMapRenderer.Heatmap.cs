using Einsatzueberwachung.Domain.Models;
using SkiaSharp;

namespace Einsatzueberwachung.Server.Services;

public sealed partial class OsmStaticMapRenderer
{
    public async Task<byte[]?> RenderCoverageHeatmapAsync(
        List<SearchAreaCoverageResult> coverageResults,
        (double Latitude, double Longitude)? elwPosition,
        int width = 1200,
        int height = 780)
    {
        var validResults = coverageResults.Where(r => r.Cells.Count > 0).ToList();
        if (validResults.Count == 0)
            return null;

        using var renderCts = new CancellationTokenSource(RenderTimeout);
        var ct = renderCts.Token;

        if (!await _globalRenderLock.WaitAsync(RenderTimeout))
        {
            _logger.LogWarning("Coverage-Heatmap: globaler Render-Lock-Timeout, überspringe Karte");
            return null;
        }

        try
        {
            var allLats = validResults.SelectMany(r => r.Coordinates.Select(c => c.Latitude)).ToList();
            var allLons = validResults.SelectMany(r => r.Coordinates.Select(c => c.Longitude)).ToList();

            if (elwPosition.HasValue) { allLats.Add(elwPosition.Value.Latitude); allLons.Add(elwPosition.Value.Longitude); }

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
            var (fullBitmap, _, _) = AssembleTileMosaic(tiles, minTileX, minTileY, maxTileX, maxTileY, TileSize);

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

                foreach (var result in validResults)
                {
                    // Zellengröße in Bildschirmpixeln (etwas größer als der geometrische Abstand, um Lücken zu vermeiden)
                    var cellPxWidth = Math.Abs(ToX(result.Cells[0].Longitude + result.CellSizeMeters / 111320.0) - ToX(result.Cells[0].Longitude));
                    var halfCell = Math.Max(cellPxWidth, 4f) * 0.65f;

                    foreach (var cell in result.Cells)
                    {
                        var cx = ToX(cell.Longitude);
                        var cy = ToY(cell.Latitude);
                        var color = cell.Covered ? new SKColor(46, 204, 113, 90) : new SKColor(231, 76, 60, 60);
                        using var cellPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true };
                        canvas.DrawRect(cx - halfCell, cy - halfCell, halfCell * 2, halfCell * 2, cellPaint);
                    }
                }

                foreach (var result in validResults)
                {
                    var areaPoints = result.Coordinates.Select(c => new SKPoint(ToX(c.Longitude), ToY(c.Latitude))).ToArray();
                    var color = ParseColor(string.IsNullOrWhiteSpace(result.SearchAreaColor) ? "#2196F3" : result.SearchAreaColor);

                    using var areaPath = new SKPath();
                    areaPath.MoveTo(areaPoints[0]);
                    for (var i = 1; i < areaPoints.Length; i++) areaPath.LineTo(areaPoints[i]);
                    areaPath.Close();

                    using var strokePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = 2.5f, IsAntialias = true };
                    canvas.DrawPath(areaPath, strokePaint);

                    var center = new SKPoint(ToX(result.Coordinates.Average(c => c.Longitude)), ToY(result.Coordinates.Average(c => c.Latitude)));
                    DrawAreaLabel(canvas, center, $"{result.SearchAreaName} — {result.CoveragePercent:F0} % abgesucht", color);
                }

                if (elwPosition.HasValue)
                    DrawMarker(canvas, new SKPoint(ToX(elwPosition.Value.Longitude), ToY(elwPosition.Value.Latitude)), new SKColor(220, 20, 60), "ELW");

                DrawAttribution(canvas, width, height, "© OpenStreetMap © CARTO");
                return EncodeAsPng(outputBitmap);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Coverage-Heatmap: Render-Timeout nach {Sec}s erreicht", RenderTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Rendern der Coverage-Heatmap");
            return null;
        }
        finally
        {
            _globalRenderLock.Release();
        }
    }
}

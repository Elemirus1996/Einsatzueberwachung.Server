using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using SkiaSharp;

namespace Einsatzueberwachung.Server.Services;

public sealed partial class OsmStaticMapRenderer
{
    public async Task<byte[]?> RenderCombinedTrackMapAsync(
        List<TeamTrackSnapshot> tracks,
        (double Latitude, double Longitude)? elwPosition,
        int width = 1200,
        int height = 780)
    {
        var validTracks = tracks.Where(track => track.Points.Count >= 2).ToList();
        if (validTracks.Count == 0)
            return null;

        using var renderCts = new CancellationTokenSource(RenderTimeout);
        var ct = renderCts.Token;

        // Globaler Lock: nur ein Map-Render gleichzeitig. Bei Timeout (z. B. wenn anderer Render hängt)
        // gibt es lieber kein Map-Bild als einen 502.
        if (!await _globalRenderLock.WaitAsync(RenderTimeout))
        {
            _logger.LogWarning("Combined-Track-Karte: globaler Render-Lock-Timeout, überspringe Karte");
            return null;
        }

        try
        {
            var allLats = validTracks.SelectMany(track => track.Points.Select(p => p.Latitude)).ToList();
            var allLons = validTracks.SelectMany(track => track.Points.Select(p => p.Longitude)).ToList();

            foreach (var areaPoint in validTracks
                .Where(track => track.SearchAreaCoordinates is { Count: >= 3 })
                .SelectMany(track => track.SearchAreaCoordinates))
            {
                allLats.Add(areaPoint.Latitude);
                allLons.Add(areaPoint.Longitude);
            }

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

                var renderedAreas = new HashSet<string>(StringComparer.Ordinal);
                foreach (var track in validTracks.Where(t => t.SearchAreaCoordinates is { Count: >= 3 }))
                {
                    var areaKey = string.Join('|', track.SearchAreaCoordinates.Select(c => $"{c.Latitude:F6},{c.Longitude:F6}"));
                    if (!renderedAreas.Add(areaKey)) continue;

                    var areaPoints = track.SearchAreaCoordinates.Select(c => new SKPoint(ToX(c.Longitude), ToY(c.Latitude))).ToArray();
                    var parsedAreaColor = ParseColor(string.IsNullOrWhiteSpace(track.SearchAreaColor) ? "#3388ff" : track.SearchAreaColor);

                    using var areaPath = new SKPath();
                    areaPath.MoveTo(areaPoints[0]);
                    for (var i = 1; i < areaPoints.Length; i++) areaPath.LineTo(areaPoints[i]);
                    areaPath.Close();

                    using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = parsedAreaColor.WithAlpha(45), IsAntialias = true };
                    canvas.DrawPath(areaPath, fillPaint);
                    using var strokePaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke, Color = parsedAreaColor, StrokeWidth = 2.5f, IsAntialias = true,
                        PathEffect = SKPathEffect.CreateDash(new[] { 12f, 6f }, 0)
                    };
                    canvas.DrawPath(areaPath, strokePaint);
                }

                foreach (var track in validTracks)
                {
                    var trackPts = track.Points.Select(p => new SKPoint(ToX(p.Longitude), ToY(p.Latitude))).ToArray();
                    using var trackPath = new SKPath();
                    trackPath.MoveTo(trackPts[0]);
                    for (var i = 1; i < trackPts.Length; i++) trackPath.LineTo(trackPts[i]);

                    using var shadowPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke, Color = new SKColor(0, 0, 0, 65), StrokeWidth = 6f,
                        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true
                    };
                    canvas.DrawPath(trackPath, shadowPaint);

                    using var trackPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke, Color = ParseColor(track.Color), StrokeWidth = 3.5f,
                        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true
                    };
                    canvas.DrawPath(trackPath, trackPaint);
                }

                if (elwPosition.HasValue)
                    DrawMarker(canvas, new SKPoint(ToX(elwPosition.Value.Longitude), ToY(elwPosition.Value.Latitude)), new SKColor(220, 20, 60), "ELW");

                DrawAttribution(canvas, width, height, "© OpenStreetMap © CARTO");
                return EncodeAsPng(outputBitmap);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Combined-Track-Karte: Render-Timeout nach {Sec}s erreicht", RenderTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Rendern der kombinierten Track-Karte");
            return null;
        }
        finally
        {
            _globalRenderLock.Release();
        }
    }

    public async Task<byte[]?> RenderSearchAreaMapAsync(
        List<SearchArea> searchAreas,
        (double Latitude, double Longitude)? elwPosition,
        MapTileType tileType = MapTileType.Streets,
        int width = 1500,
        int height = 1060)
    {
        var validAreas = searchAreas.Where(a => a.Coordinates?.Count >= 3).ToList();
        if (validAreas.Count == 0 && !elwPosition.HasValue)
            return null;

        var tileConfig = GetSearchAreaTileConfig(tileType);
        var tilePixelSize = tileConfig.PixelSize;

        using var renderCts = new CancellationTokenSource(RenderTimeout);
        var ct = renderCts.Token;

        if (!await _globalRenderLock.WaitAsync(RenderTimeout))
        {
            _logger.LogWarning("Suchgebiets-Karte: globaler Render-Lock-Timeout, überspringe Karte");
            return null;
        }

        try
        {
            var allLats = validAreas.SelectMany(a => a.Coordinates.Select(c => c.Latitude)).ToList();
            var allLons = validAreas.SelectMany(a => a.Coordinates.Select(c => c.Longitude)).ToList();

            if (elwPosition.HasValue) { allLats.Add(elwPosition.Value.Latitude); allLons.Add(elwPosition.Value.Longitude); }
            if (allLats.Count == 0) return null;

            var minLat = allLats.Min(); var maxLat = allLats.Max();
            var minLon = allLons.Min(); var maxLon = allLons.Max();

            var latPad = Math.Max((maxLat - minLat) * 0.12, 0.002);
            var lonPad = Math.Max((maxLon - minLon) * 0.12, 0.003);
            minLat -= latPad; maxLat += latPad;
            minLon -= lonPad; maxLon += lonPad;

            var zoom = CalculateZoom(minLat, maxLat, minLon, maxLon, width, height);

            var absCropLeft = LonToTileXFloat(minLon, zoom) * tilePixelSize;
            var absCropTop = LatToTileYFloat(maxLat, zoom) * tilePixelSize;
            var absCropRight = LonToTileXFloat(maxLon, zoom) * tilePixelSize;
            var absCropBottom = LatToTileYFloat(minLat, zoom) * tilePixelSize;

            AdjustCropToAspect(ref absCropLeft, ref absCropTop, ref absCropRight, ref absCropBottom, width, height);

            var minTileX = (int)Math.Floor(absCropLeft / tilePixelSize);
            var maxTileX = (int)Math.Floor(absCropRight / tilePixelSize);
            var minTileY = (int)Math.Floor(absCropTop / tilePixelSize);
            var maxTileY = (int)Math.Floor(absCropBottom / tilePixelSize);

            var tiles = await DownloadTilesWithConfigAsync(minTileX, maxTileX, minTileY, maxTileY, zoom, tileConfig.UrlTemplates, ct);
            var (fullBitmap, _, _) = AssembleTileMosaic(tiles, minTileX, minTileY, maxTileX, maxTileY, tilePixelSize);

            using (fullBitmap)
            {
                var cropLeft = (float)(absCropLeft - minTileX * tilePixelSize);
                var cropTop = (float)(absCropTop - minTileY * tilePixelSize);
                var cropRight = (float)(absCropRight - minTileX * tilePixelSize);
                var cropBottom = (float)(absCropBottom - minTileY * tilePixelSize);

                using var outputBitmap = new SKBitmap(width, height);
                using var canvas = new SKCanvas(outputBitmap);
                canvas.DrawBitmap(fullBitmap, new SKRect(cropLeft, cropTop, cropRight, cropBottom), new SKRect(0, 0, width, height));

                var scaleX = width / (cropRight - cropLeft);
                var scaleY = height / (cropBottom - cropTop);
                float ToX(double lon) => ((float)((LonToTileXFloat(lon, zoom) - minTileX) * tilePixelSize) - cropLeft) * scaleX;
                float ToY(double lat) => ((float)((LatToTileYFloat(lat, zoom) - minTileY) * tilePixelSize) - cropTop) * scaleY;

                // Erst alle Füllungen, dann alle Ränder, dann Labels
                foreach (var area in validAreas)
                {
                    var areaPoints = area.Coordinates.Select(c => new SKPoint(ToX(c.Longitude), ToY(c.Latitude))).ToArray();
                    var color = ParseColor(string.IsNullOrWhiteSpace(area.Color) ? "#2196F3" : area.Color);
                    using var areaPath = new SKPath();
                    areaPath.MoveTo(areaPoints[0]);
                    for (var i = 1; i < areaPoints.Length; i++) areaPath.LineTo(areaPoints[i]);
                    areaPath.Close();
                    using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color.WithAlpha(55), IsAntialias = true };
                    canvas.DrawPath(areaPath, fillPaint);
                }

                foreach (var area in validAreas)
                {
                    var areaPoints = area.Coordinates.Select(c => new SKPoint(ToX(c.Longitude), ToY(c.Latitude))).ToArray();
                    var color = ParseColor(string.IsNullOrWhiteSpace(area.Color) ? "#2196F3" : area.Color);
                    using var areaPath = new SKPath();
                    areaPath.MoveTo(areaPoints[0]);
                    for (var i = 1; i < areaPoints.Length; i++) areaPath.LineTo(areaPoints[i]);
                    areaPath.Close();
                    using var strokePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = 3f, IsAntialias = true };
                    canvas.DrawPath(areaPath, strokePaint);
                }

                foreach (var area in validAreas)
                {
                    var color = ParseColor(string.IsNullOrWhiteSpace(area.Color) ? "#2196F3" : area.Color);
                    DrawAreaLabel(canvas, new SKPoint(ToX(area.Coordinates.Average(c => c.Longitude)), ToY(area.Coordinates.Average(c => c.Latitude))), area.Name, color);
                }

                if (elwPosition.HasValue)
                    DrawMarker(canvas, new SKPoint(ToX(elwPosition.Value.Longitude), ToY(elwPosition.Value.Latitude)), new SKColor(220, 20, 60), "ELW");

                DrawNorthArrow(canvas, width, height);
                DrawAttribution(canvas, width, height, tileConfig.Attribution);

                using var image = SKImage.FromBitmap(outputBitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 92);
                return data.ToArray();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Suchgebiets-Karte: Render-Timeout nach {Sec}s erreicht (TileType={TileType})", RenderTimeout.TotalSeconds, tileType);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Rendern der Suchgebiets-Planungskarte");
            return null;
        }
        finally
        {
            _globalRenderLock.Release();
        }
    }

    public async Task<byte[]?> RenderSearchAreaMapAsync(
        List<SearchArea> searchAreas,
        (double Latitude, double Longitude)? elwPosition,
        MapPrintOptions options,
        List<MapMarker>? markers = null,
        List<TeamTrackSnapshot>? gpsTracks = null,
        Dictionary<string, List<TeamPhoneLocation>>? phoneTrackHistories = null,
        List<Team>? teams = null,
        int width = 1500,
        int height = 1060)
    {
        var validAreas = options.ShowSearchAreas
            ? searchAreas.Where(a => a.Coordinates?.Count >= 3).ToList()
            : new List<SearchArea>();
        var validMarkers = options.ShowPointMarkers ? (markers ?? new List<MapMarker>()) : new List<MapMarker>();
        var validTracks = options.ShowGpsTracks ? (gpsTracks?.Where(t => t.Points.Count >= 2).ToList() ?? new List<TeamTrackSnapshot>()) : new List<TeamTrackSnapshot>();
        var validPhoneTracks = options.ShowPhoneTracks ? (phoneTrackHistories ?? new Dictionary<string, List<TeamPhoneLocation>>()) : new Dictionary<string, List<TeamPhoneLocation>>();

        // Collect all coordinate points to determine bounds (if not in viewport mode)
        var allLats = new List<double>();
        var allLons = new List<double>();

        var isZoomTeam = options.ZoomMode == "team" && !string.IsNullOrWhiteSpace(options.FilterTeamId);

        if (isZoomTeam)
        {
            var targetTeamId = options.FilterTeamId!;
            foreach (var a in validAreas)
            {
                var belongs = a.AssignedTeamId == targetTeamId || 
                    (!string.IsNullOrWhiteSpace(a.AssignedTeamName) && 
                     teams != null && 
                     teams.Any(t => t.TeamId == targetTeamId && 
                                    string.Equals(t.TeamName, a.AssignedTeamName, StringComparison.OrdinalIgnoreCase)));

                if (belongs)
                {
                    allLats.AddRange(a.Coordinates.Select(c => c.Latitude));
                    allLons.AddRange(a.Coordinates.Select(c => c.Longitude));
                }
            }
            foreach (var t in validTracks)
            {
                if (t.TeamId == targetTeamId)
                {
                    allLats.AddRange(t.Points.Select(p => p.Latitude));
                    allLons.AddRange(t.Points.Select(p => p.Longitude));
                }
            }
            foreach (var (teamId, history) in validPhoneTracks)
            {
                if (teamId == targetTeamId)
                {
                    allLats.AddRange(history.Select(p => p.Latitude));
                    allLons.AddRange(history.Select(p => p.Longitude));
                }
            }
        }

        // Fallback to "all" if not zooming to team, or if the selected team has no content coordinates
        if (!isZoomTeam || allLats.Count == 0)
        {
            foreach (var a in validAreas)
            {
                allLats.AddRange(a.Coordinates.Select(c => c.Latitude));
                allLons.AddRange(a.Coordinates.Select(c => c.Longitude));
            }
            foreach (var m in validMarkers)
            {
                allLats.Add(m.Latitude);
                allLons.Add(m.Longitude);
            }
            foreach (var t in validTracks)
            {
                allLats.AddRange(t.Points.Select(p => p.Latitude));
                allLons.AddRange(t.Points.Select(p => p.Longitude));
            }
            foreach (var (_, history) in validPhoneTracks)
            {
                allLats.AddRange(history.Select(p => p.Latitude));
                allLons.AddRange(history.Select(p => p.Longitude));
            }
            if (elwPosition.HasValue) 
            { 
                allLats.Add(elwPosition.Value.Latitude); 
                allLons.Add(elwPosition.Value.Longitude); 
            }
        }

        var tileConfig = GetSearchAreaTileConfig(options.TileType);
        var tilePixelSize = tileConfig.PixelSize;

        using var renderCts = new CancellationTokenSource(RenderTimeout);
        var ct = renderCts.Token;

        if (!await _globalRenderLock.WaitAsync(RenderTimeout))
        {
            _logger.LogWarning("Erweiterte Suchgebiets-Karte: globaler Render-Lock-Timeout");
            return null;
        }

        try
        {
            double TileXToLon(double x, int z, int pixelSize)
            {
                var tileX = x / pixelSize;
                return (tileX / (1 << z)) * 360.0 - 180.0;
            }

            double TileYToLat(double y, int z, int pixelSize)
            {
                var tileY = y / pixelSize;
                var val = Math.PI * (1.0 - 2.0 * (tileY / (1 << z)));
                var e = Math.Exp(val);
                var sinLat = (e * e - 1.0) / (e * e + 1.0);
                return Math.Asin(sinLat) * 180.0 / Math.PI;
            }

            double absCropLeft, absCropTop, absCropRight, absCropBottom;
            int zoom;
            double minLat = 0, maxLat = 0, minLon = 0, maxLon = 0;

            if (options.ZoomMode == "viewport" && options.CenterLat.HasValue && options.CenterLng.HasValue && options.ZoomLevel.HasValue)
            {
                zoom = options.ZoomLevel.Value;
                if (zoom < 0) zoom = 0;
                if (zoom > 19) zoom = 19;

                var centerXPixels = LonToTileXFloat(options.CenterLng.Value, zoom) * tilePixelSize;
                var centerYPixels = LatToTileYFloat(options.CenterLat.Value, zoom) * tilePixelSize;

                absCropLeft = centerXPixels - width / 2.0;
                absCropRight = centerXPixels + width / 2.0;
                absCropTop = centerYPixels - height / 2.0;
                absCropBottom = centerYPixels + height / 2.0;
            }
            else
            {
                if (allLats.Count == 0) return null;

                var tempMinLat = allLats.Min(); var tempMaxLat = allLats.Max();
                var tempMinLon = allLons.Min(); var tempMaxLon = allLons.Max();

                var latPad = Math.Max((tempMaxLat - tempMinLat) * 0.12, 0.002);
                var lonPad = Math.Max((tempMaxLon - tempMinLon) * 0.12, 0.003);
                tempMinLat -= latPad; tempMaxLat += latPad;
                tempMinLon -= lonPad; tempMaxLon += lonPad;

                zoom = CalculateZoom(tempMinLat, tempMaxLat, tempMinLon, tempMaxLon, width, height);

                absCropLeft = LonToTileXFloat(tempMinLon, zoom) * tilePixelSize;
                absCropTop = LatToTileYFloat(tempMaxLat, zoom) * tilePixelSize;
                absCropRight = LonToTileXFloat(tempMaxLon, zoom) * tilePixelSize;
                absCropBottom = LatToTileYFloat(tempMinLat, zoom) * tilePixelSize;

                AdjustCropToAspect(ref absCropLeft, ref absCropTop, ref absCropRight, ref absCropBottom, width, height);
            }

            // Compute final lat/lon bounds corresponding to the crop area for accurate grid drawing
            minLon = TileXToLon(absCropLeft, zoom, tilePixelSize);
            maxLon = TileXToLon(absCropRight, zoom, tilePixelSize);
            maxLat = TileYToLat(absCropTop, zoom, tilePixelSize);
            minLat = TileYToLat(absCropBottom, zoom, tilePixelSize);

            var minTileX = (int)Math.Floor(absCropLeft / tilePixelSize);
            var maxTileX = (int)Math.Floor(absCropRight / tilePixelSize);
            var minTileY = (int)Math.Floor(absCropTop / tilePixelSize);
            var maxTileY = (int)Math.Floor(absCropBottom / tilePixelSize);

            var tiles = await DownloadTilesWithConfigAsync(minTileX, maxTileX, minTileY, maxTileY, zoom, tileConfig.UrlTemplates, ct);
            var (fullBitmap, _, _) = AssembleTileMosaic(tiles, minTileX, minTileY, maxTileX, maxTileY, tilePixelSize);

            using (fullBitmap)
            {
                var cropLeft = (float)(absCropLeft - minTileX * tilePixelSize);
                var cropTop = (float)(absCropTop - minTileY * tilePixelSize);
                var cropRight = (float)(absCropRight - minTileX * tilePixelSize);
                var cropBottom = (float)(absCropBottom - minTileY * tilePixelSize);

                using var outputBitmap = new SKBitmap(width, height);
                using var canvas = new SKCanvas(outputBitmap);
                canvas.DrawBitmap(fullBitmap, new SKRect(cropLeft, cropTop, cropRight, cropBottom), new SKRect(0, 0, width, height));

                var scaleX = width / (cropRight - cropLeft);
                var scaleY = height / (cropBottom - cropTop);
                float ToX(double lon) => ((float)((LonToTileXFloat(lon, zoom) - minTileX) * tilePixelSize) - cropLeft) * scaleX;
                float ToY(double lat) => ((float)((LatToTileYFloat(lat, zoom) - minTileY) * tilePixelSize) - cropTop) * scaleY;

                // Draw search areas (fill, then stroke, then labels)
                foreach (var area in validAreas)
                {
                    var areaPoints = area.Coordinates.Select(c => new SKPoint(ToX(c.Longitude), ToY(c.Latitude))).ToArray();
                    var color = ParseColor(string.IsNullOrWhiteSpace(area.Color) ? "#2196F3" : area.Color);
                    using var areaPath = new SKPath();
                    areaPath.MoveTo(areaPoints[0]);
                    for (var i = 1; i < areaPoints.Length; i++) areaPath.LineTo(areaPoints[i]);
                    areaPath.Close();
                    using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color.WithAlpha(55), IsAntialias = true };
                    canvas.DrawPath(areaPath, fillPaint);
                }
                foreach (var area in validAreas)
                {
                    var areaPoints = area.Coordinates.Select(c => new SKPoint(ToX(c.Longitude), ToY(c.Latitude))).ToArray();
                    var color = ParseColor(string.IsNullOrWhiteSpace(area.Color) ? "#2196F3" : area.Color);
                    using var areaPath = new SKPath();
                    areaPath.MoveTo(areaPoints[0]);
                    for (var i = 1; i < areaPoints.Length; i++) areaPath.LineTo(areaPoints[i]);
                    areaPath.Close();
                    using var strokePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = 3f, IsAntialias = true };
                    canvas.DrawPath(areaPath, strokePaint);
                }
                foreach (var area in validAreas)
                {
                    var color = ParseColor(string.IsNullOrWhiteSpace(area.Color) ? "#2196F3" : area.Color);
                    DrawAreaLabel(canvas, new SKPoint(ToX(area.Coordinates.Average(c => c.Longitude)), ToY(area.Coordinates.Average(c => c.Latitude))), area.Name, color);
                }

                // Draw GPS tracks
                foreach (var track in validTracks)
                {
                    var trackPts = track.Points.Select(p => new SKPoint(ToX(p.Longitude), ToY(p.Latitude))).ToArray();
                    using var trackPath = new SKPath();
                    trackPath.MoveTo(trackPts[0]);
                    for (var i = 1; i < trackPts.Length; i++) trackPath.LineTo(trackPts[i]);

                    using var shadowPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke, Color = new SKColor(0, 0, 0, 65), StrokeWidth = 6f,
                        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true
                    };
                    canvas.DrawPath(trackPath, shadowPaint);

                    using var trackPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke, Color = ParseColor(track.Color), StrokeWidth = 3.5f,
                        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true
                    };
                    canvas.DrawPath(trackPath, trackPaint);
                }

                // Draw phone tracks
                var phoneColors = new[] { "#4CAF50", "#FF9800", "#9C27B0", "#00BCD4", "#E91E63", "#3F51B5" };
                var phoneColorIdx = 0;
                var teamLookup = teams?.ToDictionary(t => t.TeamId, t => t.TeamName);
                foreach (var (teamId, history) in validPhoneTracks)
                {
                    var validPoints = history.Where(p => p.Latitude != 0 || p.Longitude != 0).ToList();
                    if (validPoints.Count < 2) continue;

                    var phonePts = validPoints.Select(p => new SKPoint(ToX(p.Longitude), ToY(p.Latitude))).ToArray();
                    using var phonePath = new SKPath();
                    phonePath.MoveTo(phonePts[0]);
                    for (var i = 1; i < phonePts.Length; i++) phonePath.LineTo(phonePts[i]);

                    var phoneColor = phoneColors[phoneColorIdx++ % phoneColors.Length];
                    using var phonePaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke, Color = ParseColor(phoneColor), StrokeWidth = 2.5f,
                        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true,
                        PathEffect = SKPathEffect.CreateDash(new[] { 8f, 4f }, 0)
                    };
                    canvas.DrawPath(phonePath, phonePaint);

                    // Draw team label at last point
                    var teamName = teamLookup != null && teamLookup.TryGetValue(teamId, out var tn) ? tn : null;
                    if (!string.IsNullOrWhiteSpace(teamName))
                    {
                        var lastPt = phonePts[^1];
                        DrawMarker(canvas, lastPt, ParseColor(phoneColor), teamName);
                    }
                }

                // Draw point markers
                foreach (var marker in validMarkers)
                {
                    var markerColor = ParseColor(string.IsNullOrWhiteSpace(marker.Color) ? "#2196F3" : marker.Color);
                    var label = string.IsNullOrWhiteSpace(marker.Label) ? "●" : marker.Label;
                    DrawMarker(canvas, new SKPoint(ToX(marker.Longitude), ToY(marker.Latitude)), markerColor, label);
                }

                // Draw ELW marker
                if (elwPosition.HasValue)
                    DrawMarker(canvas, new SKPoint(ToX(elwPosition.Value.Longitude), ToY(elwPosition.Value.Latitude)), new SKColor(220, 20, 60), "ELW");

                if (!string.IsNullOrEmpty(options.GridType) && options.GridType != "none")
                    DrawGrid(canvas, ToX, ToY, minLat, maxLat, minLon, maxLon, width, height, options.GridType);

                DrawNorthArrow(canvas, width, height);
                DrawAttribution(canvas, width, height, tileConfig.Attribution);

                using var image = SKImage.FromBitmap(outputBitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 92);
                return data.ToArray();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Erweiterte Suchgebiets-Karte: Render-Timeout nach {Sec}s erreicht", RenderTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Rendern der erweiterten Suchgebiets-Karte");
            return null;
        }
        finally
        {
            _globalRenderLock.Release();
        }
    }
}

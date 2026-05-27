using SkiaSharp;

namespace Einsatzueberwachung.Server.Services;

public sealed partial class OsmStaticMapRenderer
{
    private static void DrawMarker(SKCanvas canvas, SKPoint center, SKColor color, string label)
    {
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White, IsAntialias = true };
        canvas.DrawCircle(center, 10, borderPaint);

        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true };
        canvas.DrawCircle(center, 8, fillPaint);

        using var labelFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 13);
        using var bgPaint = new SKPaint { Color = new SKColor(255, 255, 255, 200), IsAntialias = true };
        labelFont.MeasureText(label, out var lblBounds);
        canvas.DrawRoundRect(center.X + 13, center.Y - lblBounds.Height / 2 - 2, lblBounds.Width + 6, lblBounds.Height + 4, 3, 3, bgPaint);

        using var textPaint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(label, center.X + 16, center.Y + lblBounds.Height / 2 - 1, SKTextAlign.Left, labelFont, textPaint);
    }

    private static void DrawAreaLabel(SKCanvas canvas, SKPoint center, string text, SKColor areaColor)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 16);
        font.MeasureText(text, out var bounds);

        const float pad = 5f;
        var bgRect = new SKRoundRect(
            new SKRect(
                center.X - bounds.Width / 2f - pad,
                center.Y - bounds.Height / 2f - pad,
                center.X + bounds.Width / 2f + pad,
                center.Y + bounds.Height / 2f + pad),
            4f, 4f);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 255, 255, 215), IsAntialias = true };
        canvas.DrawRoundRect(bgRect, bgPaint);

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = areaColor, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawRoundRect(bgRect, borderPaint);

        using var textPaint = new SKPaint { Color = new SKColor(30, 30, 30), IsAntialias = true };
        canvas.DrawText(text, center.X, center.Y + bounds.Height / 2f - 2f, SKTextAlign.Center, font, textPaint);
    }

    private static void DrawNorthArrow(SKCanvas canvas, int width, int height)
    {
        var cx = width - 38f;
        var cy = 42f;
        const float r = 26f;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 255, 255, 200), IsAntialias = true };
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(120, 120, 120), StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawCircle(cx, cy, r, bgPaint);
        canvas.DrawCircle(cx, cy, r, borderPaint);

        using var arrowPath = new SKPath();
        arrowPath.MoveTo(cx, cy - r + 7f);
        arrowPath.LineTo(cx - 7f, cy + 6f);
        arrowPath.LineTo(cx + 7f, cy + 6f);
        arrowPath.Close();
        using var arrowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(50, 80, 200), IsAntialias = true };
        canvas.DrawPath(arrowPath, arrowPaint);

        using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
        using var nFont = new SKFont(typeface, 12f);
        using var nPaint = new SKPaint { Color = new SKColor(50, 80, 200), IsAntialias = true };
        canvas.DrawText("N", cx, cy - r + 6f, SKTextAlign.Center, nFont, nPaint);
    }

    private static void DrawAttribution(SKCanvas canvas, int width, int height, string attribution)
    {
        using var font = new SKFont(SKTypeface.Default, 10);
        using var paint = new SKPaint { Color = new SKColor(80, 80, 80), IsAntialias = true };
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 255, 255, 180) };
        font.MeasureText(attribution, out var textBounds);
        var attrX = width - textBounds.Width - 6;
        var attrY = height - 6;
        canvas.DrawRect(attrX - 3, attrY - textBounds.Height - 2, textBounds.Width + 6, textBounds.Height + 4, bgPaint);
        canvas.DrawText(attribution, attrX, attrY, SKTextAlign.Left, font, paint);
    }

    private static void DrawGrid(SKCanvas canvas, Func<double, float> toX, Func<double, float> toY,
        double minLat, double maxLat, double minLon, double maxLon, int width, int height, string gridType)
    {
        if (gridType == "latlon")
            DrawLatLonGrid(canvas, toX, toY, minLat, maxLat, minLon, maxLon, width, height);
        else if (gridType == "utm")
            DrawUtmGrid(canvas, toX, toY, minLat, maxLat, minLon, maxLon, width, height);
    }

    private static void DrawLatLonGrid(SKCanvas canvas, Func<double, float> toX, Func<double, float> toY,
        double minLat, double maxLat, double minLon, double maxLon, int width, int height)
    {
        var extent = Math.Max(maxLat - minLat, maxLon - minLon);
        double interval = extent switch
        {
            > 5.0 => 1.0,
            > 1.0 => 0.5,
            > 0.5 => 0.1,
            > 0.1 => 0.05,
            > 0.05 => 0.01,
            > 0.01 => 0.005,
            _ => 0.001
        };
        int decimals = interval >= 1.0 ? 0 : interval >= 0.1 ? 1 : interval >= 0.01 ? 2 : interval >= 0.001 ? 3 : 4;

        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(214, 51, 132, 100),
            StrokeWidth = 1f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 8f, 4f }, 0)
        };
        using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 10f);
        using var textPaint = new SKPaint { Color = new SKColor(160, 20, 100, 220), IsAntialias = true };
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 255, 255, 180) };

        // Horizontal lines (constant lat)
        var firstLat = Math.Ceiling(minLat / interval) * interval;
        for (var lat = firstLat; lat <= maxLat + interval * 0.01; lat += interval)
        {
            var y = toY(lat);
            if (y < -20 || y > height + 20) continue;
            canvas.DrawLine(0, y, width, y, linePaint);
            var label = $"{lat.ToString($"F{decimals}")}°N";
            font.MeasureText(label, out var lb);
            canvas.DrawRect(3, y - lb.Height - 6, lb.Width + 8, lb.Height + 4, bgPaint);
            canvas.DrawText(label, 7, y - 6, SKTextAlign.Left, font, textPaint);
        }

        // Vertical lines (constant lon)
        var firstLon = Math.Ceiling(minLon / interval) * interval;
        for (var lon = firstLon; lon <= maxLon + interval * 0.01; lon += interval)
        {
            var x = toX(lon);
            if (x < -20 || x > width + 20) continue;
            canvas.DrawLine(x, 0, x, height, linePaint);
            var label = $"{lon.ToString($"F{decimals}")}°E";
            font.MeasureText(label, out var lb);
            canvas.DrawRect(x - lb.Width / 2 - 3, height - lb.Height - 9, lb.Width + 8, lb.Height + 4, bgPaint);
            canvas.DrawText(label, x, height - 9, SKTextAlign.Center, font, textPaint);
        }
    }

    private static void DrawUtmGrid(SKCanvas canvas, Func<double, float> toX, Func<double, float> toY,
        double minLat, double maxLat, double minLon, double maxLon, int width, int height)
    {
        var centerLat = (minLat + maxLat) / 2;
        var centerLon = (minLon + maxLon) / 2;
        var (zone, band, centerE, centerN) = Domain.Services.UtmConverter.LatLongToUtm(centerLat, centerLon);

        var (_, _, minE, _) = Domain.Services.UtmConverter.LatLongToUtm(centerLat, minLon);
        var (_, _, maxE, _) = Domain.Services.UtmConverter.LatLongToUtm(centerLat, maxLon);
        var (_, _, _, minN) = Domain.Services.UtmConverter.LatLongToUtm(minLat, centerLon);
        var (_, _, _, maxN) = Domain.Services.UtmConverter.LatLongToUtm(maxLat, centerLon);
        if (minE > maxE) (minE, maxE) = (maxE, minE);
        if (minN > maxN) (minN, maxN) = (maxN, minN);

        var extent = Math.Max(maxE - minE, maxN - minN);
        double interval = extent switch
        {
            > 500000 => 100000,
            > 100000 => 50000,
            > 50000 => 10000,
            > 10000 => 5000,
            > 5000 => 1000,
            > 1000 => 500,
            > 500 => 100,
            _ => 50
        };

        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0, 100, 255, 100),
            StrokeWidth = 1f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 8f, 4f }, 0)
        };
        using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
        using var font = new SKFont(typeface, 10f);
        using var textPaint = new SKPaint { Color = new SKColor(0, 60, 200, 220), IsAntialias = true };
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 255, 255, 180) };

        string FormatE(double e) => interval switch
        {
            >= 1000 => $"E{(long)Math.Round(e / 1000)}km",
            >= 100 => $"E{e / 1000:F1}km",
            >= 10 => $"E{e / 1000:F2}km",
            _ => $"E{(long)e}m"
        };
        string FormatN(double n) => interval switch
        {
            >= 1000 => $"N{(long)Math.Round(n / 1000)}km",
            >= 100 => $"N{n / 1000:F1}km",
            >= 10 => $"N{n / 1000:F2}km",
            _ => $"N{(long)n}m"
        };

        // Easting lines (approx vertical — use centerN latitude for lon mapping)
        // Start one interval before minE to cover grid lines just outside the padded extent
        var firstE = Math.Ceiling((minE - interval) / interval) * interval;
        for (var e = firstE; e <= maxE + interval; e += interval)
        {
            var (_, lonAtE) = Domain.Services.UtmConverter.UtmToLatLong(zone, band, e, centerN);
            var x = toX(lonAtE);
            if (x < -20 || x > width + 20) continue;
            canvas.DrawLine(x, 0, x, height, linePaint);
            var label = FormatE(e);
            font.MeasureText(label, out var lb);
            canvas.DrawRect(x - lb.Width / 2 - 3, height - lb.Height - 9, lb.Width + 8, lb.Height + 4, bgPaint);
            canvas.DrawText(label, x, height - 9, SKTextAlign.Center, font, textPaint);
        }

        // Northing lines (approx horizontal — use centerLon for lat mapping)
        // Start one interval before minN to cover grid lines just outside the padded extent
        var firstN = Math.Ceiling((minN - interval) / interval) * interval;
        for (var n = firstN; n <= maxN + interval; n += interval)
        {
            var (latAtN, _) = Domain.Services.UtmConverter.UtmToLatLong(zone, band, centerE, n);
            var y = toY(latAtN);
            if (y < -20 || y > height + 20) continue;
            canvas.DrawLine(0, y, width, y, linePaint);
            var label = FormatN(n);
            font.MeasureText(label, out var lb);
            canvas.DrawRect(3, y - lb.Height - 6, lb.Width + 8, lb.Height + 4, bgPaint);
            canvas.DrawText(label, 7, y - 6, SKTextAlign.Left, font, textPaint);
        }
    }


    private static SKColor ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length < 7)
            return new SKColor(255, 68, 68);

        try
        {
            hex = hex.TrimStart('#');
            return new SKColor(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }
        catch
        {
            return new SKColor(255, 68, 68);
        }
    }

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
}

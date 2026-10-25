using System.Globalization;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Positionen der App „Traccar Client“ (OsmAnd-Protokoll, https://www.traccar.org/osmand/): Formular-POST bzw.
/// Query-Parameter mit id, lat, lon, timestamp, accuracy, batt. Die App puffert bei Funkloch und sendet mit
/// Originalzeit nach – so entsteht der Laufweg auch bei gesperrtem Handy.
/// </summary>
public static class TraccarProtocol
{
    public sealed record Position(string DeviceId, double Latitude, double Longitude, DateTime? TimestampUtc, double? Accuracy);

    public static Position? Parse(Func<string, string?> value)
    {
        var id = value("id") ?? value("deviceid");
        if (string.IsNullOrWhiteSpace(id)) return null;

        double lat, lon;
        if (value("location") is { } location)
        {
            // Variante „lat,lon“
            var parts = location.Split(',');
            if (parts.Length != 2 || !TryDouble(parts[0], out lat) || !TryDouble(parts[1], out lon)) return null;
        }
        else if (!TryDouble(value("lat"), out lat) || !TryDouble(value("lon"), out lon))
        {
            return null;
        }

        if (lat is < -90 or > 90 || lon is < -180 or > 180) return null;

        double? accuracy = TryDouble(value("accuracy"), out var acc) && acc >= 0 ? acc : null;
        return new Position(id.Trim(), lat, lon, ParseTimestamp(value("timestamp")), accuracy);
    }

    /// <summary>Sekunden oder Millisekunden seit 1970, ISO 8601 oder „yyyy-MM-dd HH:mm:ss“ (UTC).</summary>
    public static DateTime? ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            try
            {
                return number > 100_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(number).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(number).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    private static bool TryDouble(string? raw, out double result) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        && !double.IsNaN(result) && !double.IsInfinity(result);
}

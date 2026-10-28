using System.Globalization;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Positionen der App „Traccar Client“ (OsmAnd-Protokoll, https://www.traccar.org/osmand/): Formular-POST bzw.
/// Query-Parameter mit id, lat, lon, timestamp, accuracy, batt – oder JSON (neuere App-Versionen, siehe ParseJson). Die App puffert bei Funkloch und sendet mit
/// Originalzeit nach – so entsteht der Laufweg auch bei gesperrtem Handy.
/// </summary>
public static class TraccarProtocol
{
    /// <summary>
    /// Einrichtungslink fuer Traccar Client (Team-Seite und Traccar-Test). Die Werte kommen aus den Einstellungen –
    /// Traccar uebernimmt sie bei jedem Antippen neu; danach laesst sich die App vom Server nicht mehr umstellen.
    /// </summary>
    public static string EinrichtungsLink(string endpoint, string id, Einsatzueberwachung.Domain.Models.AppSettings einstellungen)
    {
        var genauigkeit = einstellungen.TraccarGenauigkeit is "highest" or "high" or "medium" or "low"
            ? einstellungen.TraccarGenauigkeit : "high";
        return "org.traccar.client://?url=" + Uri.EscapeDataString(endpoint)
            + "&id=" + Uri.EscapeDataString(id)
            + "&accuracy=" + genauigkeit
            + "&interval=" + Math.Clamp(einstellungen.TraccarIntervallSekunden, 5, 300).ToString(CultureInfo.InvariantCulture)
            + "&distance=" + Math.Clamp(einstellungen.TraccarMindestabstandMeter, 0, 500).ToString(CultureInfo.InvariantCulture)
            + "&angle=30&buffer=true"
            + "&wakelock=" + (einstellungen.TraccarWachhalten ? "true" : "false")
            + "&stop_detection=" + (einstellungen.TraccarStillstandErkennen ? "true" : "false");
    }

    public sealed record Position(string DeviceId, double Latitude, double Longitude, DateTime? TimestampUtc, double? Accuracy, int? BatteryPercent = null);

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
        // batt: Prozent (Traccar Client), teils als Kommazahl
        int? battery = TryDouble(value("batt"), out var batt) && batt is >= 0 and <= 100 ? (int)Math.Round(batt) : null;
        return new Position(id.Trim(), lat, lon, ParseTimestamp(value("timestamp")), accuracy, battery);
    }

    /// <summary>
    /// JSON-Variante der neueren Traccar-Client-Apps (wie im Traccar-Server, OsmAndProtocolDecoder.decodeJson):
    /// {"device_id": "…", "location": {"timestamp": "ISO", "coords": {"latitude": …, "longitude": …, "accuracy": …}}}
    /// </summary>
    public static Position? ParseJson(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

            var id = root.TryGetProperty("device_id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.String ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)
                || !root.TryGetProperty("location", out var location) || location.ValueKind != System.Text.Json.JsonValueKind.Object
                || !location.TryGetProperty("coords", out var coords) || coords.ValueKind != System.Text.Json.JsonValueKind.Object
                || !Zahl(coords, "latitude", out var lat) || !Zahl(coords, "longitude", out var lon))
            {
                return null;
            }

            if (lat is < -90 or > 90 || lon is < -180 or > 180) return null;

            double? accuracy = Zahl(coords, "accuracy", out var acc) && acc >= 0 ? acc : null;
            var timestamp = location.TryGetProperty("timestamp", out var ts)
                ? ParseTimestamp(ts.ValueKind == System.Text.Json.JsonValueKind.String ? ts.GetString() : ts.GetRawText())
                : null;
            // battery.level: 0–1 (wie im Traccar-Server)
            int? battery = location.TryGetProperty("battery", out var bat) && bat.ValueKind == System.Text.Json.JsonValueKind.Object
                && Zahl(bat, "level", out var level) && level is >= 0 and <= 1 ? (int)Math.Round(level * 100) : null;
            return new Position(id.Trim(), lat, lon, timestamp, accuracy, battery);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        static bool Zahl(System.Text.Json.JsonElement e, string name, out double wert)
        {
            wert = 0;
            return e.TryGetProperty(name, out var el) && el.ValueKind == System.Text.Json.JsonValueKind.Number
                && el.TryGetDouble(out wert) && !double.IsNaN(wert) && !double.IsInfinity(wert);
        }
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

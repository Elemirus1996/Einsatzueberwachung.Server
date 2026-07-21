using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services;

/// <summary>
/// Zentraler Builder für GPX-Dateien. Enthält alle GPX-Formatierungslogik,
/// damit keine doppelte Implementierung in den Razor-Komponenten nötig ist.
/// </summary>
public static class GpxBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string Creator = "Einsatzueberwachung.Server";
    private const int GarminFileNameLength = 20;

    // -------------------------------------------------------------------------
    // Hilfsmethoden
    // -------------------------------------------------------------------------

    private static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static void AppendHeader(StringBuilder sb, bool includeGpxx = false)
    {
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<gpx version=\"1.1\" creator=\"{Creator}\"");
        sb.AppendLine("     xmlns=\"http://www.topografix.com/GPX/1/1\"");
        if (includeGpxx)
            sb.AppendLine("     xmlns:gpxx=\"http://www.garmin.com/xmlschemas/GpxExtensions/v3\"");
        sb.AppendLine("     xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"");
        sb.AppendLine("     xsi:schemaLocation=\"http://www.topografix.com/GPX/1/1 http://www.topografix.com/GPX/1/1/gpx.xsd\">");
    }

    // -------------------------------------------------------------------------
    // Suchgebiet (geschlossenes Polygon-Track, Magenta)
    // -------------------------------------------------------------------------

    /// <summary>Erzeugt den GPX-Inhalt für ein Suchgebiet als geschlossenes Polygon.</summary>
    public static string BuildSearchAreaGpx(SearchArea area)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var safeName = XmlEscape(area.Name);

        var sb = new StringBuilder();
        AppendHeader(sb, includeGpxx: true);
        sb.AppendLine("  <trk>");
        sb.AppendLine($"    <name>{safeName}</name>");
        sb.AppendLine($"    <desc>Suchgebiet: {safeName}</desc>");
        sb.AppendLine("    <extensions>");
        sb.AppendLine("      <gpxx:TrackExtension>");
        sb.AppendLine("        <gpxx:DisplayColor>Magenta</gpxx:DisplayColor>");
        sb.AppendLine("      </gpxx:TrackExtension>");
        sb.AppendLine("    </extensions>");
        sb.AppendLine("    <trkseg>");

        foreach (var coord in area.Coordinates)
        {
            var lat = coord.Latitude.ToString("F7", Inv);
            var lon = coord.Longitude.ToString("F7", Inv);
            sb.AppendLine($"      <trkpt lat=\"{lat}\" lon=\"{lon}\"><time>{timestamp}</time></trkpt>");
        }

        // Polygon schließen: ersten Punkt wiederholen
        var first = area.Coordinates[0];
        var firstLat = first.Latitude.ToString("F7", Inv);
        var firstLon = first.Longitude.ToString("F7", Inv);
        sb.AppendLine($"      <trkpt lat=\"{firstLat}\" lon=\"{firstLon}\"><time>{timestamp}</time></trkpt>");

        sb.AppendLine("    </trkseg>");
        sb.AppendLine("  </trk>");
        sb.AppendLine("</gpx>");
        return sb.ToString();
    }

    /// <summary>Gibt den Dateinamen für das GPX eines Suchgebiets zurück.</summary>
    public static string SearchAreaFileName(SearchArea area, DateTime? date = null)
    {
        var dateStamp = (date ?? DateTime.Now).ToString("ddMM", Inv);
        var areaName = area.Name.Replace(" ", "_");
        var availableNameLength = GarminFileNameLength - dateStamp.Length - 1;
        var shortenedAreaName = areaName.Substring(0, Math.Min(areaName.Length, availableNameLength));

        return $"{dateStamp}_{shortenedAreaName}.gpx";
    }

    // -------------------------------------------------------------------------
    // GPS-Track-Snapshot
    // -------------------------------------------------------------------------

    /// <summary>Erzeugt den GPX-Inhalt für einen aufgezeichneten GPS-Track (Halsband).</summary>
    public static string BuildTrackSnapshotGpx(TeamTrackSnapshot snap)
    {
        var namePart = snap.TeamName +
                       (string.IsNullOrEmpty(snap.SearchAreaName) ? "" : $"_{snap.SearchAreaName}");
        var safeName = XmlEscape(namePart);

        var sb = new StringBuilder();
        AppendHeader(sb);
        sb.AppendLine("  <trk>");
        sb.AppendLine($"    <name>{safeName}</name>");
        sb.AppendLine($"    <desc>Team: {safeName} · {snap.FormattedDistance} · {snap.FormattedDuration}</desc>");
        sb.AppendLine("    <trkseg>");

        foreach (var pt in snap.Points)
        {
            var iso = pt.Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            var lat = pt.Latitude.ToString("F7", Inv);
            var lon = pt.Longitude.ToString("F7", Inv);
            sb.AppendLine($"      <trkpt lat=\"{lat}\" lon=\"{lon}\">");
            sb.AppendLine($"        <time>{iso}</time>");
            sb.AppendLine("      </trkpt>");
        }

        sb.AppendLine("    </trkseg>");
        sb.AppendLine("  </trk>");
        sb.AppendLine("</gpx>");
        return sb.ToString();
    }

    /// <summary>Gibt den Dateinamen für den GPX-Track eines Snapshots zurück.</summary>
    public static string TrackSnapshotFileName(TeamTrackSnapshot snap)
    {
        var namePart = snap.TeamName +
                       (string.IsNullOrEmpty(snap.SearchAreaName) ? "" : $"_{snap.SearchAreaName}");
        return namePart.Replace(" ", "_") + "_Track.gpx";
    }

    // -------------------------------------------------------------------------
    // Koordinaten-Marker (Wegpunkte)
    // -------------------------------------------------------------------------

    /// <summary>Erzeugt den GPX-Inhalt für einen einzelnen Marker als Wegpunkt.</summary>
    public static string BuildMarkerGpx(MapMarker marker)
    {
        var sb = new StringBuilder();
        AppendHeader(sb);
        AppendWaypoint(sb, marker);
        sb.AppendLine("</gpx>");
        return sb.ToString();
    }

    /// <summary>Erzeugt den GPX-Inhalt für mehrere Marker als Wegpunkte.</summary>
    public static string BuildMarkersGpx(IEnumerable<MapMarker> markers)
    {
        var sb = new StringBuilder();
        AppendHeader(sb);
        foreach (var marker in markers)
            AppendWaypoint(sb, marker);
        sb.AppendLine("</gpx>");
        return sb.ToString();
    }

    /// <summary>Gibt den Dateinamen für einen einzelnen Marker zurück.</summary>
    public static string MarkerFileName(MapMarker marker, string dateStamp) =>
        $"Punkt_{marker.Label.Replace(" ", "_")}_{dateStamp}.gpx";

    private static void AppendWaypoint(StringBuilder sb, MapMarker marker)
    {
        var lat = marker.Latitude.ToString("F6", Inv);
        var lon = marker.Longitude.ToString("F6", Inv);
        var name = XmlEscape(marker.Label);
        sb.AppendLine($"  <wpt lat=\"{lat}\" lon=\"{lon}\">");
        sb.AppendLine($"    <name>{name}</name>");
        sb.AppendLine($"    <time>{marker.CreatedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}</time>");
        sb.AppendLine("  </wpt>");
    }
}

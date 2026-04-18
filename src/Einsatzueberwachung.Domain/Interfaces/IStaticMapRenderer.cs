using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Interfaces;

/// <summary>
/// Rendert statische Kartenbilder (OSM-Tiles) mit Track- und Suchgebiet-Overlays
/// für den PDF-Export.
/// </summary>
public interface IStaticMapRenderer
{
    /// <summary>
    /// Rendert eine Karte mit GPS-Track und optionalem Suchgebiet als PNG-Byte-Array.
    /// </summary>
    Task<byte[]?> RenderTrackMapAsync(
        List<TrackPoint> trackPoints,
        List<(double Latitude, double Longitude)>? searchAreaCoords,
        string trackColor,
        string? areaColor,
        int width = 800,
        int height = 450);

    /// <summary>
    /// Rendert eine kombinierte Karte mit allen GPS-Tracks, Suchgebieten und optionalem ELW-Marker.
    /// </summary>
    Task<byte[]?> RenderCombinedTrackMapAsync(
        List<TeamTrackSnapshot> tracks,
        (double Latitude, double Longitude)? elwPosition,
        int width = 1200,
        int height = 780);
}

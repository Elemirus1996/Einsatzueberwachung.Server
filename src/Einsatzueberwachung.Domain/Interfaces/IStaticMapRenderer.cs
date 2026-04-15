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
}

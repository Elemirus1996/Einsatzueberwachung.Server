using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

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

    /// <summary>
    /// Rendert eine Planungskarte mit allen Suchgebieten, Gebietsbeschriftungen und ELW-Marker.
    /// Wird für den Einsatzkarten-PDF-Ausdruck verwendet (vor dem Ausrücken der Teams).
    /// </summary>
    Task<byte[]?> RenderSearchAreaMapAsync(
        List<SearchArea> searchAreas,
        (double Latitude, double Longitude)? elwPosition,
        MapTileType tileType = MapTileType.Streets,
        int width = 1500,
        int height = 1060);

    /// <summary>
    /// Rendert eine Planungskarte mit erweiterten Layer-Optionen (Marker, GPS-Tracks, Handy-Tracks).
    /// </summary>
    Task<byte[]?> RenderSearchAreaMapAsync(
        List<SearchArea> searchAreas,
        (double Latitude, double Longitude)? elwPosition,
        MapPrintOptions options,
        List<MapMarker>? markers = null,
        List<TeamTrackSnapshot>? gpsTracks = null,
        Dictionary<string, List<TeamPhoneLocation>>? phoneTrackHistories = null,
        List<Team>? teams = null,
        int width = 1500,
        int height = 1060);
}

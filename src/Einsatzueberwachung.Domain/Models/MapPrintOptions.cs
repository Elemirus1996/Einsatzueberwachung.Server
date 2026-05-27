using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Models;

/// <summary>
/// Options for the Einsatzkarte PDF print, controlling which layers are visible.
/// </summary>
public class MapPrintOptions
{
    public MapTileType TileType { get; set; } = MapTileType.Streets;
    public string? FilterTeamId { get; set; }
    public bool ShowSearchAreas { get; set; } = true;
    public bool ShowPointMarkers { get; set; } = true;
    public bool ShowGpsTracks { get; set; } = false;
    public bool ShowPhoneTracks { get; set; } = false;
    /// <summary>Koordinatengitter: "none", "latlon" oder "utm"</summary>
    public string GridType { get; set; } = "none";
    public string ZoomMode { get; set; } = "all"; // "all", "team", "viewport"
    public double? CenterLat { get; set; }
    public double? CenterLng { get; set; }
    public int? ZoomLevel { get; set; }
}

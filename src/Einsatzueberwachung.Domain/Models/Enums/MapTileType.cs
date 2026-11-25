namespace Einsatzueberwachung.Domain.Models.Enums;

public enum MapTileType
{
    Streets,
    Satellite,
    Topographic,
    /// <summary>Amtliche Luftbilder der Länder (DOP20), alle Länder zusammen</summary>
    Luftbild
    // SatelliteGoogle und Hybrid entfernt (#210): Google-Kacheln ohne Vertrag nicht erlaubt
}

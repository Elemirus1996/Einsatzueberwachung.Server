// Repräsentiert einen Koordinaten-Marker auf der Karte
// Kann per Mausklick oder durch Eingabe von Dezimal-/UTM-Koordinaten erstellt werden

using System;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Models
{
    public class MapMarker
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Color { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Team, das den Punkt per App gesetzt hat (docs/APP_API.md Team-Rolle Abschnitt 13); null = Einsatzleitung</summary>
        public string? TeamId { get; set; }
        
        /// <summary>
        /// UTM-Zone (z.B. "32U"), wird aus Lat/Long berechnet
        /// </summary>
        public string UtmZone { get; set; }
        
        /// <summary>
        /// UTM Ostwert (Easting) in Metern
        /// </summary>
        public double UtmEasting { get; set; }
        
        /// <summary>
        /// UTM Nordwert (Northing) in Metern
        /// </summary>
        public double UtmNorthing { get; set; }

        public MapMarker()
        {
            Id = Guid.NewGuid().ToString();
            Label = string.Empty;
            Description = string.Empty;
            Color = "#2196F3";
            CreatedAt = Ortszeit.Jetzt;
            UtmZone = string.Empty;
        }

        public string FormattedLatLng => $"{Latitude:F6}° / {Longitude:F6}°";
        public string FormattedUtm => !string.IsNullOrWhiteSpace(UtmZone)
            ? $"{UtmZone} {UtmEasting:F0} E / {UtmNorthing:F0} N"
            : "";
        public string FormattedTimestamp => CreatedAt.ToString("HH:mm:ss");
    }
}

// GPS-Position eines Halsbands zu einem bestimmten Zeitpunkt (Breadcrumb)
// Wird chronologisch gespeichert um den Pfad des Hundes auf der Karte zu zeichnen

using System;

namespace Einsatzueberwachung.Domain.Models
{
    public class CollarLocation
    {
        public string CollarId { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime Timestamp { get; set; }

        public CollarLocation()
        {
            CollarId = string.Empty;
            Latitude = 0;
            Longitude = 0;
            Timestamp = DateTime.UtcNow;
        }

        public CollarLocation(string collarId, double latitude, double longitude, DateTime timestamp)
        {
            CollarId = collarId;
            Latitude = latitude;
            Longitude = longitude;
            Timestamp = timestamp;
        }
    }
}

// Aufgezeichneter GPS-Track eines Halsbands, gesichert beim Stoppen eines Teams
// Wird für den Einsatzbericht und PDF-Export verwendet

using System;
using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>
    /// Snapshot eines aufgezeichneten GPS-Tracks (Halsband-Pfad)
    /// </summary>
    public class TeamTrackSnapshot
    {
        public string CollarId { get; set; } = string.Empty;
        public string CollarName { get; set; } = string.Empty;
        public string TeamId { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public string SearchAreaName { get; set; } = string.Empty;
        public string Color { get; set; } = "#FF4444";
        public DateTime CapturedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Koordinaten des zugewiesenen Suchgebiets (für PDF-Darstellung)
        /// </summary>
        public List<(double Latitude, double Longitude)> SearchAreaCoordinates { get; set; } = new();

        /// <summary>
        /// Farbe des Suchgebiets
        /// </summary>
        public string SearchAreaColor { get; set; } = string.Empty;

        /// <summary>
        /// Screenshot der Leaflet-Karte als Base64-PNG (für PDF-Export)
        /// </summary>
        public string? MapImageBase64 { get; set; }

        /// <summary>
        /// Chronologische Liste der GPS-Punkte
        /// </summary>
        public List<TrackPoint> Points { get; set; } = new();

        /// <summary>
        /// Gesamte Streckenlänge in Metern (approximiert)
        /// </summary>
        public double TotalDistanceMeters
        {
            get
            {
                if (Points.Count < 2) return 0;
                double total = 0;
                for (int i = 1; i < Points.Count; i++)
                {
                    total += HaversineDistance(Points[i - 1].Latitude, Points[i - 1].Longitude,
                                              Points[i].Latitude, Points[i].Longitude);
                }
                return total;
            }
        }

        public string FormattedDistance
        {
            get
            {
                var m = TotalDistanceMeters;
                return m < 1000 ? $"{m:N0} m" : $"{m / 1000.0:N2} km";
            }
        }

        public TimeSpan Duration => Points.Count >= 2
            ? Points[^1].Timestamp - Points[0].Timestamp
            : TimeSpan.Zero;

        public string FormattedDuration
        {
            get
            {
                var d = Duration;
                return d.TotalHours >= 1
                    ? $"{(int)d.TotalHours}h {d.Minutes}min"
                    : $"{d.Minutes}min {d.Seconds}s";
            }
        }

        private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000; // Erdradius in Metern
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }
    }

    /// <summary>
    /// Einzelner GPS-Punkt eines Tracks
    /// </summary>
    public class TrackPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

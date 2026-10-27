// Quelle: WPF-Projekt Models/SearchArea.cs
// Repräsentiert ein Suchgebiet auf der Karte mit Polygon-Koordinaten und Team-Zuordnung

using System;
using System.Collections.Generic;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Models
{
    public class SearchArea
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string AssignedTeamId { get; set; }
        public string AssignedTeamName { get; set; }
        public string Color { get; set; }
        public bool IsCompleted { get; set; }
        public string Notes { get; set; }
        public List<(double Latitude, double Longitude)> Coordinates { get; set; }
        public string GeoJsonData { get; set; } // Speichert das komplette GeoJSON für Leaflet
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public SearchArea()
        {
            Id = Guid.NewGuid().ToString();
            Name = string.Empty;
            AssignedTeamId = string.Empty;
            AssignedTeamName = string.Empty;
            Color = "#2196F3";
            IsCompleted = false;
            Notes = string.Empty;
            Coordinates = new List<(double, double)>();
            GeoJsonData = string.Empty;
            CreatedAt = Ortszeit.Jetzt;
        }

        public double AreaInSquareMeters
        {
            get
            {
                if (Coordinates == null || Coordinates.Count < 3)
                    return 0;

                // Verwende die Shoelace-Formel für Polygone auf der Erdoberfläche
                // Annäherung: Für kleine Flächen (< 100km²) ist dies ausreichend genau
                return CalculatePolygonArea(Coordinates);
            }
        }
        
        private double CalculatePolygonArea(List<(double Latitude, double Longitude)> coords)
        {
            if (coords.Count < 3)
                return 0;

            const double EarthRadius = 6371000.0; // Erdradius in Metern
            
            double area = 0;
            int n = coords.Count;

            for (int i = 0; i < n; i++)
            {
                var p1 = coords[i];
                var p2 = coords[(i + 1) % n];

                // Konvertiere zu Radiant
                double lat1 = p1.Latitude * Math.PI / 180.0;
                double lat2 = p2.Latitude * Math.PI / 180.0;
                double lon1 = p1.Longitude * Math.PI / 180.0;
                double lon2 = p2.Longitude * Math.PI / 180.0;

                // Berechne Fläche mit sphärischem Exzess
                area += (lon2 - lon1) * (2 + Math.Sin(lat1) + Math.Sin(lat2));
            }

            area = Math.Abs(area * EarthRadius * EarthRadius / 2.0);
            
            return area;
        }

        public double AreaInHectares => AreaInSquareMeters / 10000.0;
        public double AreaInSquareKilometers => AreaInSquareMeters / 1000000.0;

        public string FormattedArea
        {
            get
            {
                var sqm = AreaInSquareMeters;
                
                if (sqm < 1)
                    return "< 1 m²";
                else if (sqm < 50000)
                    return $"{sqm:N0} m²";
                else if (sqm < 1000000)
                    return $"{AreaInHectares:N2} ha";
                else
                    return $"{AreaInSquareKilometers:N2} km²";
            }
        }
    }
}

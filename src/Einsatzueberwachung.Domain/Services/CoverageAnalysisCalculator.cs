// Reine Geometrie-Berechnung der Flächenabdeckung eines Suchgebiets aus GPS-Tracks.
// Analog zu UtmConverter: zustandslos, keine DI nötig.

using System;
using System.Collections.Generic;
using System.Linq;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    public static class CoverageAnalysisCalculator
    {
        private const double MetersPerDegreeLat = 111320.0;

        /// <summary>
        /// Berechnet pro Suchgebiet (gruppiert nach dem zum Zeitpunkt der Aufzeichnung zugewiesenen Polygon)
        /// ein Raster aus Zellen und markiert jede Zelle als abgesucht, wenn ihr Mittelpunkt innerhalb von
        /// <paramref name="coverageRadiusMeters"/> zu mindestens einem GPS-Track-Punkt liegt.
        /// </summary>
        public static List<SearchAreaCoverageResult> Calculate(
            IEnumerable<TeamTrackSnapshot> tracks,
            double coverageRadiusMeters = 25,
            double cellSizeMeters = 15,
            int maxCellsPerArea = 4000)
        {
            var results = new List<SearchAreaCoverageResult>();
            var renderedAreas = new HashSet<string>(StringComparer.Ordinal);

            foreach (var group in tracks.Where(t => t.SearchAreaCoordinates is { Count: >= 3 }))
            {
                var areaKey = string.Join('|', group.SearchAreaCoordinates.Select(c => $"{c.Latitude:F6},{c.Longitude:F6}"));
                if (!renderedAreas.Add(areaKey))
                    continue;

                var areaTracks = tracks.Where(t =>
                    t.SearchAreaCoordinates is { Count: >= 3 } &&
                    string.Join('|', t.SearchAreaCoordinates.Select(c => $"{c.Latitude:F6},{c.Longitude:F6}")) == areaKey);

                var points = areaTracks.SelectMany(t => t.Points).ToList();
                if (points.Count == 0)
                    continue;

                var result = CalculateForArea(group.SearchAreaCoordinates, group.SearchAreaName, group.SearchAreaColor,
                    points, coverageRadiusMeters, cellSizeMeters, maxCellsPerArea);
                if (result != null)
                    results.Add(result);
            }

            return results;
        }

        private static SearchAreaCoverageResult? CalculateForArea(
            List<(double Latitude, double Longitude)> polygon,
            string areaName,
            string areaColor,
            List<TrackPoint> points,
            double coverageRadiusMeters,
            double cellSizeMeters,
            int maxCellsPerArea)
        {
            var minLat = polygon.Min(c => c.Latitude);
            var maxLat = polygon.Max(c => c.Latitude);
            var minLon = polygon.Min(c => c.Longitude);
            var maxLon = polygon.Max(c => c.Longitude);

            var midLat = (minLat + maxLat) / 2.0;
            var lonScale = Math.Cos(midLat * Math.PI / 180.0);
            if (lonScale < 0.01) lonScale = 0.01;
            var metersPerDegreeLon = MetersPerDegreeLat * lonScale;

            var widthMeters = (maxLon - minLon) * metersPerDegreeLon;
            var heightMeters = (maxLat - minLat) * MetersPerDegreeLat;
            if (widthMeters <= 0 || heightMeters <= 0)
                return null;

            var effectiveCellSize = cellSizeMeters;
            var estimatedCells = (widthMeters / effectiveCellSize) * (heightMeters / effectiveCellSize);
            if (estimatedCells > maxCellsPerArea)
            {
                effectiveCellSize = Math.Sqrt(widthMeters * heightMeters / maxCellsPerArea);
            }

            var latStep = effectiveCellSize / MetersPerDegreeLat;
            var lonStep = effectiveCellSize / metersPerDegreeLon;

            var cells = new List<CoverageCell>();
            var totalCells = 0;
            var coveredCells = 0;

            for (var lat = minLat + latStep / 2.0; lat <= maxLat; lat += latStep)
            {
                for (var lon = minLon + lonStep / 2.0; lon <= maxLon; lon += lonStep)
                {
                    if (!IsPointInPolygon(lat, lon, polygon))
                        continue;

                    totalCells++;
                    var covered = points.Any(p => HaversineDistance(lat, lon, p.Latitude, p.Longitude) <= coverageRadiusMeters);
                    if (covered) coveredCells++;

                    cells.Add(new CoverageCell { Latitude = lat, Longitude = lon, Covered = covered });
                }
            }

            if (totalCells == 0)
                return null;

            return new SearchAreaCoverageResult
            {
                SearchAreaName = areaName,
                SearchAreaColor = areaColor,
                Coordinates = polygon,
                CellSizeMeters = effectiveCellSize,
                CoverageRadiusMeters = coverageRadiusMeters,
                TotalCells = totalCells,
                CoveredCells = coveredCells,
                Cells = cells
            };
        }

        /// <summary>Ray-Casting-Test: liegt (lat, lon) innerhalb des Polygons?</summary>
        private static bool IsPointInPolygon(double lat, double lon, List<(double Latitude, double Longitude)> polygon)
        {
            var inside = false;
            var n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                var intersects = (pi.Longitude > lon) != (pj.Longitude > lon) &&
                    lat < (pj.Latitude - pi.Latitude) * (lon - pi.Longitude) / (pj.Longitude - pi.Longitude) + pi.Latitude;

                if (intersects)
                    inside = !inside;
            }
            return inside;
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
}

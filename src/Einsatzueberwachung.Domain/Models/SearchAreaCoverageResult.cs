// Ergebnis der Flächenabdeckungs-Analyse eines Suchgebiets, berechnet aus GPS-Tracks
// Wird für den Einsatzbericht (PDF) verwendet

using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>
    /// Grid-basierte Flächenabdeckung eines Suchgebiets, ermittelt aus den aufgezeichneten GPS-Tracks.
    /// </summary>
    public class SearchAreaCoverageResult
    {
        public string SearchAreaName { get; set; } = string.Empty;
        public string SearchAreaColor { get; set; } = string.Empty;
        public List<(double Latitude, double Longitude)> Coordinates { get; set; } = new();

        public double CellSizeMeters { get; set; }
        public double CoverageRadiusMeters { get; set; }

        public int TotalCells { get; set; }
        public int CoveredCells { get; set; }

        public double CoveragePercent => TotalCells == 0 ? 0 : (double)CoveredCells / TotalCells * 100.0;

        public List<CoverageCell> Cells { get; set; } = new();
    }

    /// <summary>
    /// Einzelne Rasterzelle innerhalb eines Suchgebiets-Polygons.
    /// </summary>
    public class CoverageCell
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool Covered { get; set; }
    }
}

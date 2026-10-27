using System;
using System.Collections.Generic;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>
    /// Eine pixel-basierte Lagekarte für Trümmer-Einsätze (z. B. Drohnen-Luftbild).
    /// Bilddateien liegen relativ zum Datenverzeichnis unter <c>truemmer/{einsatzId}/{KarteId}.{ext}</c>.
    /// </summary>
    public class TruemmerKarte
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string ImageRelativePath { get; set; } = string.Empty;
        public int ImageWidthPx { get; set; }
        public int ImageHeightPx { get; set; }
        public DateTime UploadedAt { get; set; } = Ortszeit.Jetzt;
    }

    /// <summary>
    /// Suchgebiet auf einer <see cref="TruemmerKarte"/> — Koordinaten in Pixeln (X, Y),
    /// Origin top-left des Bildes. Bewusst getrennt von <see cref="SearchArea"/> (GPS).
    /// </summary>
    public class TruemmerArea
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TruemmerKarteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? AssignedTeamId { get; set; }
        public string? AssignedTeamName { get; set; }
        public List<TruemmerPoint> Points { get; set; } = new();
        public string Color { get; set; } = "#FF9800";
        public string? Note { get; set; }
        public bool IsCompleted { get; set; }
    }

    public class TruemmerPoint
    {
        public double X { get; set; }
        public double Y { get; set; }

        public TruemmerPoint() { }
        public TruemmerPoint(double x, double y) { X = x; Y = y; }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Einsatzueberwachung.Domain.Models.Divera
{
    public class DiveraMember
    {
        public int Id { get; set; }
        public string Firstname { get; set; } = string.Empty;
        public string Lastname { get; set; } = string.Empty;
        /// <summary>Name aus den Stammdaten (Feld „Divera Benutzer-ID“) – hat Vorrang vor dem Namen aus Divera</summary>
        public string? StammdatenName { get; set; }
        public string FullName => !string.IsNullOrWhiteSpace(StammdatenName) ? StammdatenName : $"{Firstname} {Lastname}".Trim();
        public int StatusId { get; set; }

        /// <summary>Name des Status aus Divera status_sorter (z.B. "30 Minuten", "1 Stunde")</summary>
        public string StatusName { get; set; } = string.Empty;

        /// <summary>Hex-Farbe des Status aus Divera status_sorter (z.B. "00FF00", "FFFF00", "FF0000")</summary>
        public string StatusColor { get; set; } = string.Empty;

        public List<int> QualificationIds { get; set; } = new();

        // Verfuegbarkeit wird aus der Hex-Farbe abgeleitet, nicht aus festen IDs
        public bool IsVerfuegbar => DiveraStatusFarbe.IsGreen(StatusColor);
        public bool IsBedingtVerfuegbar => DiveraStatusFarbe.IsYellow(StatusColor);
        public bool IsNichtVerfuegbar => DiveraStatusFarbe.IsRed(StatusColor);

        /// <summary>Bootstrap-Badge-CSS aus Hex-Farbe abgeleitet</summary>
        public string StatusBadgeCss => StatusColor switch
        {
            _ when DiveraStatusFarbe.IsGreen(StatusColor) => "bg-success",
            _ when DiveraStatusFarbe.IsYellow(StatusColor) => "bg-warning text-dark",
            _ when DiveraStatusFarbe.IsRed(StatusColor) => "bg-danger",
            _ => "bg-secondary"
        };

        /// <summary>Zeigt den echten Divera-Statusnamen aus der API</summary>
        public string StatusText => string.IsNullOrWhiteSpace(StatusName) ? "Unbekannt" : StatusName;
    }
}

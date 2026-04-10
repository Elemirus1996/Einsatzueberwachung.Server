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
        public string FullName => $"{Firstname} {Lastname}".Trim();
        public int StatusId { get; set; }

        /// <summary>Name des Status aus Divera status_sorter (z.B. "30 Minuten", "1 Stunde")</summary>
        public string StatusName { get; set; } = string.Empty;

        /// <summary>Hex-Farbe des Status aus Divera status_sorter (z.B. "00FF00", "FFFF00", "FF0000")</summary>
        public string StatusColor { get; set; } = string.Empty;

        public List<int> QualificationIds { get; set; } = new();

        // Verfuegbarkeit wird aus der Hex-Farbe abgeleitet, nicht aus festen IDs
        public bool IsVerfuegbar => IsGreenColor(StatusColor);
        public bool IsBedingtVerfuegbar => IsYellowColor(StatusColor);
        public bool IsNichtVerfuegbar => IsRedColor(StatusColor);

        /// <summary>Bootstrap-Badge-CSS aus Hex-Farbe abgeleitet</summary>
        public string StatusBadgeCss => StatusColor switch
        {
            _ when IsGreenColor(StatusColor) => "bg-success",
            _ when IsYellowColor(StatusColor) => "bg-warning text-dark",
            _ when IsRedColor(StatusColor) => "bg-danger",
            _ => "bg-secondary"
        };

        /// <summary>Zeigt den echten Divera-Statusnamen aus der API</summary>
        public string StatusText => string.IsNullOrWhiteSpace(StatusName) ? "Unbekannt" : StatusName;

        // --- Hilfsmethoden zur Farb-Analyse ---

        private static bool ParseHex(string hex, out int r, out int g, out int b)
        {
            r = g = b = 0;
            hex = (hex ?? string.Empty).TrimStart('#');
            if (hex.Length < 6) return false;
            return int.TryParse(hex[..2], NumberStyles.HexNumber, null, out r)
                && int.TryParse(hex[2..4], NumberStyles.HexNumber, null, out g)
                && int.TryParse(hex[4..6], NumberStyles.HexNumber, null, out b);
        }

        private static bool IsGreenColor(string hex)
        {
            if (!ParseHex(hex, out int r, out int g, out int b)) return false;
            return g > r && g > b && g > 80;
        }

        private static bool IsYellowColor(string hex)
        {
            if (!ParseHex(hex, out int r, out int g, out int b)) return false;
            return r > 150 && g > 150 && b < 100;
        }

        private static bool IsRedColor(string hex)
        {
            if (!ParseHex(hex, out int r, out int g, out int b)) return false;
            return r > g && r > b && r > 80;
        }
    }
}

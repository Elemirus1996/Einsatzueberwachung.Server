using System.Globalization;

namespace Einsatzueberwachung.Domain.Models.Divera
{
    /// <summary>
    /// Bedeutung eines Divera-Status fuer die Einsatzleitung. Die Status selbst (Namen, IDs, Farben) legt jede
    /// Einheit in Divera eigen fest – deshalb wird die Bedeutung aus der Farbe abgeleitet oder in den
    /// Divera-Einstellungen von Hand zugeordnet, nie aus festen IDs.
    /// </summary>
    public enum DiveraRueckmeldung
    {
        Unbekannt = 0,
        Kommt = 1,
        KommtSpaeter = 2,
        KommtNicht = 3
    }

    public static class DiveraStatusFarbe
    {
        /// <summary>Gruen = kommt/verfuegbar, Gelb/Orange = spaeter/bedingt, Rot = kommt nicht.</summary>
        public static DiveraRueckmeldung Bedeutung(string? hex)
        {
            if (IsGreen(hex)) return DiveraRueckmeldung.Kommt;
            if (IsYellow(hex)) return DiveraRueckmeldung.KommtSpaeter;
            if (IsRed(hex)) return DiveraRueckmeldung.KommtNicht;
            return DiveraRueckmeldung.Unbekannt;
        }

        public static string BadgeCss(DiveraRueckmeldung bedeutung) => bedeutung switch
        {
            DiveraRueckmeldung.Kommt => "bg-success",
            DiveraRueckmeldung.KommtSpaeter => "bg-warning text-dark",
            DiveraRueckmeldung.KommtNicht => "bg-danger",
            _ => "bg-secondary"
        };

        public static string Anzeige(DiveraRueckmeldung bedeutung) => bedeutung switch
        {
            DiveraRueckmeldung.Kommt => "Kommt",
            DiveraRueckmeldung.KommtSpaeter => "Kommt später",
            DiveraRueckmeldung.KommtNicht => "Kommt nicht",
            _ => "Nicht zugeordnet"
        };

        private static bool ParseHex(string? hex, out int r, out int g, out int b)
        {
            r = g = b = 0;
            hex = (hex ?? string.Empty).TrimStart('#');
            if (hex.Length < 6) return false;
            return int.TryParse(hex[..2], NumberStyles.HexNumber, null, out r)
                && int.TryParse(hex[2..4], NumberStyles.HexNumber, null, out g)
                && int.TryParse(hex[4..6], NumberStyles.HexNumber, null, out b);
        }

        // Reihenfolge wichtig: Gelb (hohes Rot UND Gruen) vor Rot pruefen
        public static bool IsYellow(string? hex) =>
            ParseHex(hex, out var r, out var g, out var b) && r > 150 && g > 150 && b < 100;

        public static bool IsGreen(string? hex) =>
            ParseHex(hex, out var r, out var g, out var b) && g > r && g > b && g > 80;

        public static bool IsRed(string? hex) =>
            ParseHex(hex, out var r, out var g, out var b) && r > g && r > b && r > 80 && !IsYellow(hex);
    }
}

namespace Einsatzueberwachung.Domain.Models.Divera
{
    public class DiveraUcrEntry
    {
        public int MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        /// <summary>Org-spezifische Status-ID (z.B. 56298) oder 0 fuer keine Antwort</summary>
        public int Status { get; set; }
        /// <summary>Status-Name aus StatusDefinitions (z.B. "30 Minuten", "In 1 Std."). Leer wenn unbekannt.</summary>
        public string StatusName { get; set; } = string.Empty;
        public string StatusColor { get; set; } = string.Empty;

        public bool IsPositive => Status switch
        {
            1 or 3 or 56296 or 56297 => true,
            0 or 2 or 56298 => false,
            _ => IsPositiveStatusColor(StatusColor)
        };

        public string StatusText => Status switch
        {
            0 => "Keine Rückmeldung",
            56296 => string.IsNullOrWhiteSpace(StatusName) ? "30 Minuten" : StatusName,
            56297 => string.IsNullOrWhiteSpace(StatusName) ? "1 Stunde" : StatusName,
            56298 => string.IsNullOrWhiteSpace(StatusName) ? "Nicht einsatzbereit" : StatusName,
            // Pull/all liefert ggf. 1/2/3 fuer einfache Faelle
            1 => string.IsNullOrWhiteSpace(StatusName) ? "Kommt" : StatusName,
            2 => string.IsNullOrWhiteSpace(StatusName) ? "Kommt nicht" : StatusName,
            3 => string.IsNullOrWhiteSpace(StatusName) ? "Kommt später" : StatusName,
            // Org-spezifische Status-IDs (z.B. 56298): StatusName nutzen wenn bekannt, sonst "Hat geantwortet"
            _ => string.IsNullOrWhiteSpace(StatusName) ? "Hat geantwortet" : StatusName
        };

        public string StatusBadgeCss => Status switch
        {
            0 => "bg-secondary",
            2 or 56298 => "bg-danger",
            3 or 56297 => "bg-warning text-dark",
            56296 => "bg-success",
            _ when IsPositive => "bg-success",
            _ => "bg-secondary"
        };

        private static bool IsPositiveStatusColor(string color)
        {
            color = color.TrimStart('#');
            if (color.Length < 6
                || !byte.TryParse(color[..2], System.Globalization.NumberStyles.HexNumber, null, out var red)
                || !byte.TryParse(color[2..4], System.Globalization.NumberStyles.HexNumber, null, out var green)
                || !byte.TryParse(color[4..6], System.Globalization.NumberStyles.HexNumber, null, out var blue))
                return false;

            var isGreen = green > red && green > blue && green > 80;
            var isYellow = red > 150 && green > 150 && blue < 100;
            return isGreen || isYellow;
        }
    }
}

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

        public string StatusText => Status switch
        {
            0 => "Keine Rückmeldung",
            56296 => string.IsNullOrEmpty(StatusName) ? "30 Minuten" : StatusName,
            56297 => string.IsNullOrEmpty(StatusName) ? "1 Stunde" : StatusName,
            56298 => string.IsNullOrEmpty(StatusName) ? "Nicht einsatzbereit" : StatusName,
            // Pull/all liefert ggf. 1/2/3 fuer einfache Faelle
            1 => string.IsNullOrEmpty(StatusName) ? "Kommt" : StatusName,
            2 => string.IsNullOrEmpty(StatusName) ? "Kommt nicht" : StatusName,
            3 => string.IsNullOrEmpty(StatusName) ? "Kommt später" : StatusName,
            // Org-spezifische Status-IDs (z.B. 56298): StatusName nutzen wenn bekannt, sonst "Hat geantwortet"
            _ => string.IsNullOrEmpty(StatusName) ? "Hat geantwortet" : StatusName
        };

        public string StatusBadgeCss => Status switch
        {
            0 => "bg-secondary",
            56296 => "bg-success",
            56297 => "bg-warning text-dark",
            56298 => "bg-danger",
            2 => "bg-danger",
            3 => "bg-warning text-dark",
            _ => "bg-success"  // 1 = Kommt, alle org-spezifischen = als geantwortet = gruen
        };
    }
}

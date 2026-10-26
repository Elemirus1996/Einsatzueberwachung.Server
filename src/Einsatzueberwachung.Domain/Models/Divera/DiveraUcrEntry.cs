namespace Einsatzueberwachung.Domain.Models.Divera
{
    public class DiveraUcrEntry
    {
        public int MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        /// <summary>Status-ID der eigenen Divera-Einheit oder 0 fuer keine Antwort</summary>
        public int Status { get; set; }
        /// <summary>Status-Name aus den Status-Definitionen der Einheit (z.B. "30 Minuten"). Leer wenn unbekannt.</summary>
        public string StatusName { get; set; } = string.Empty;
        /// <summary>Hex-Farbe des Status aus Divera</summary>
        public string StatusColor { get; set; } = string.Empty;
        /// <summary>Bedeutung (aus Farbe oder Zuordnung in den Divera-Einstellungen)</summary>
        public DiveraRueckmeldung Bedeutung { get; set; }

        public string StatusText => Status switch
        {
            0 => "Keine Rückmeldung",
            _ when !string.IsNullOrWhiteSpace(StatusName) => StatusName,
            // Aeltere Schnittstelle liefert ggf. 1/2/3 ohne Definition
            1 => "Kommt",
            2 => "Kommt nicht",
            3 => "Kommt später",
            _ => "Hat geantwortet"
        };

        public string StatusBadgeCss => Status == 0 ? "bg-secondary" : DiveraStatusFarbe.BadgeCss(Bedeutung);
    }
}

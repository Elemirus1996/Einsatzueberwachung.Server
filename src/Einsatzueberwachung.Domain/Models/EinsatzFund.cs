namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>
    /// Angaben zum Fund der vermissten Person für den Einsatzbericht. Abgefragt beim Einsatzende, wenn das Ergebnis ein Fund ist.
    /// </summary>
    public class EinsatzFund
    {
        /// <summary>Fundzeit in UTC.</summary>
        public DateTime? ZeitUtc { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string Ort { get; set; } = string.Empty;
        public string UebergabeAn { get; set; } = string.Empty;
        /// <summary>Team, das gefunden hat (aus dem Fund-Punkt); null = nicht bekannt.</summary>
        public string? TeamId { get; set; }

        public bool IstLeer => ZeitUtc is null && Latitude is null && string.IsNullOrWhiteSpace(Ort) && string.IsNullOrWhiteSpace(UebergabeAn);
    }
}

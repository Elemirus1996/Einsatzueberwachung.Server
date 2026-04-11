// Divera 24/7 - Status-Definition (Verfuegbarkeitsstatus)

namespace Einsatzueberwachung.Domain.Models.Divera
{
    /// <summary>
    /// Definition eines Verfuegbarkeitsstatus in Divera 24/7 (aus cluster.status_sorter)
    /// </summary>
    public class DiveraStatusDefinition
    {
        public int Id { get; set; }

        /// <summary>
        /// Vollstaendiger Name, z.B. "Verfuegbar"
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Kurzbezeichnung, z.B. "v"
        /// </summary>
        public string Shortname { get; set; } = string.Empty;

        /// <summary>
        /// Hex-Farbe, z.B. "#00FF00"
        /// </summary>
        public string Color { get; set; } = string.Empty;

        public bool ShowInAvailability { get; set; }
    }
}

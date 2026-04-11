// Divera 24/7 - Mitglied mit Verfuegbarkeitsstatus

using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models.Divera
{
    /// <summary>
    /// Divera-Mitglied mit aktuellem Verfuegbarkeitsstatus
    /// </summary>
    public class DiveraMember
    {
        public int Id { get; set; }
        public string Firstname { get; set; } = string.Empty;
        public string Lastname { get; set; } = string.Empty;

        /// <summary>
        /// Qualifikations-IDs laut Divera
        /// </summary>
        public List<int> Qualifications { get; set; } = new();

        /// <summary>
        /// Aktueller Verfuegbarkeitsstatus-ID (aus cluster.status)
        /// </summary>
        public int StatusId { get; set; }

        /// <summary>
        /// Lesbarer Status-Text (aus status_sorter befuellt)
        /// </summary>
        public string StatusText { get; set; } = string.Empty;

        /// <summary>
        /// Hex-Farbe des Status (aus status_sorter befuellt)
        /// </summary>
        public string StatusColor { get; set; } = string.Empty;

        public string FullName => $"{Firstname} {Lastname}".Trim();

        /// <summary>
        /// Gibt Bootstrap-Badge-Klasse basierend auf dem Verfuegbarkeitsstatus zurueck.
        /// Standard-Status-IDs: 1/2 = verfuegbar, 3 = bedingt, 4 = nicht verfuegbar, 0 = kein Status
        /// </summary>
        public string GetVerfuegbarkeitBadgeClass()
        {
            return StatusId switch
            {
                1 or 2 => "bg-success",
                3 => "bg-warning text-dark",
                4 => "bg-danger",
                _ => "bg-secondary"
            };
        }

        /// <summary>
        /// Gibt Bootstrap-Icon-Klasse basierend auf dem Verfuegbarkeitsstatus zurueck.
        /// </summary>
        public string GetVerfuegbarkeitIcon()
        {
            return StatusId switch
            {
                1 or 2 => "bi-check-circle-fill text-success",
                3 => "bi-dash-circle-fill text-warning",
                4 => "bi-x-circle-fill text-danger",
                _ => "bi-circle text-secondary"
            };
        }
    }
}

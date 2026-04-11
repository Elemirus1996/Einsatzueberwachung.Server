// Divera 24/7 - Aufbereitete Antwort des pull/all-Endpunkts

using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models.Divera
{
    /// <summary>
    /// Aufbereitete (gemappte) Antwort des Divera pull/all-Endpunkts.
    /// Enthaelt Alarme, Mitglieder mit Verfuegbarkeitsstatus und Status-Definitionen.
    /// </summary>
    public class DiveraPullResponse
    {
        /// <summary>
        /// Name der Einheit/Staffel laut Divera
        /// </summary>
        public string EinheitName { get; set; } = string.Empty;

        /// <summary>
        /// Aktive (nicht geschlossene) Alarme
        /// </summary>
        public List<DiveraAlarm> AktiveAlarme { get; set; } = new();

        /// <summary>
        /// Alle Alarme (inkl. geschlossene)
        /// </summary>
        public List<DiveraAlarm> AlleAlarme { get; set; } = new();

        /// <summary>
        /// Mitglieder mit aktuellem Verfuegbarkeitsstatus
        /// </summary>
        public List<DiveraMember> Mitglieder { get; set; } = new();

        /// <summary>
        /// Status-Definitionen der Einheit
        /// </summary>
        public List<DiveraStatusDefinition> StatusDefinitionen { get; set; } = new();
    }
}

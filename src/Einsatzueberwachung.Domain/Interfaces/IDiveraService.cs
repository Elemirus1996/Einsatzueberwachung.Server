// Service-Interface fuer die Divera 24/7 API-Integration

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Models.Divera;

namespace Einsatzueberwachung.Domain.Interfaces
{
    /// <summary>
    /// Interface fuer den Divera 24/7 API-Service.
    /// Benoetigt den Einheiten-Zugangscode (NICHT den persoenlichen Benutzer-API-Schluessel)
    /// fuer den Zugriff auf alle Alarme und Mitglieder der Einheit.
    /// Den Einheiten-Zugangscode findest du in Divera unter: Verwaltung → Schnittstellen → API.
    /// </summary>
    public interface IDiveraService
    {
        /// <summary>
        /// Gibt an, ob der Divera-Service konfiguriert ist (Enabled = true und AccessKey vorhanden)
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Wird ausgeloest, wenn sich Divera-Daten geaendert haben (nach erfolgreichem Polling)
        /// </summary>
        event Action? DataChanged;

        /// <summary>
        /// Ruft alle aktiven (nicht geschlossenen) Alarme ab
        /// </summary>
        Task<List<DiveraAlarm>> GetActiveAlarmsAsync();

        /// <summary>
        /// Ruft einen einzelnen Alarm anhand der ID ab (inkl. Rueckmeldungen)
        /// </summary>
        Task<DiveraAlarm?> GetAlarmByIdAsync(int alarmId);

        /// <summary>
        /// Ruft alle Mitglieder mit ihrem aktuellen Verfuegbarkeitsstatus ab
        /// </summary>
        Task<List<DiveraMember>> GetMembersWithStatusAsync();

        /// <summary>
        /// Ruft nur die verfuegbaren Mitglieder ab (StatusId 1 oder 2)
        /// </summary>
        Task<List<DiveraMember>> GetAvailableMembersAsync();

        /// <summary>
        /// Ruft alle Daten auf einmal ab (effizientester Endpunkt fuer Polling).
        /// Ergebnis wird 60 Sekunden gecacht.
        /// </summary>
        Task<DiveraPullResponse?> PullAllAsync();

        /// <summary>
        /// Testet die Verbindung zur Divera API.
        /// Gibt true zurueck wenn der AccessKey gueltig ist.
        /// </summary>
        Task<bool> TestConnectionAsync();
    }
}

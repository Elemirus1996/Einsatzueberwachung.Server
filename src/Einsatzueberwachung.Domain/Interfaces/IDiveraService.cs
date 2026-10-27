// Divera 24/7 API Service Interface
// Ermoeglicht Abruf von Alarmen und Verfuegbarkeitsstatus

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Models.Divera;

namespace Einsatzueberwachung.Domain.Interfaces
{
    public interface IDiveraService
    {
        bool IsConfigured { get; }
        bool HasActiveAlarms { get; }
        int PollIntervalIdleSeconds { get; }
        int PollIntervalActiveSeconds { get; }
        Task<List<DiveraAlarm>> GetActiveAlarmsAsync();
        Task<DiveraAlarm?> GetAlarmByIdAsync(int alarmId);
        /// <summary>Ruft den letzten aktiven Alarm ueber /api/last-alarm ab (zuverlaessig mit Staffel-API-Key).</summary>
        Task<DiveraAlarm?> GetLastAlarmAsync();
        Task<List<DiveraMember>> GetMembersWithStatusAsync();
        Task<List<DiveraMember>> GetAvailableMembersAsync();
        Task<DiveraPullResponse?> PullAllAsync();
        Task<bool> TestConnectionAsync();
        /// <summary>Ruft die rohen JSON-Antworten beider Endpunkte ab (zur Diagnose).</summary>
        Task<Dictionary<string, string>> GetRawDiagnosticAsync();

        /// <summary>
        /// Wird aufgerufen wenn sich die Konfiguration aendert (z.B. API-Key geaendert).
        /// Der Service liest daraufhin die neuen Einstellungen aus ISettingsService.
        /// </summary>
        Task RefreshConfigurationAsync();

        event Action? DataChanged;
    }
}

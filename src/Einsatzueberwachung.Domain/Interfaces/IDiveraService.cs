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

        /// <summary>Divera-Funktionen (Einstellungen → Divera), jeweils nur wenn Divera eingerichtet ist</summary>
        /// <summary>Einstellungen laden (falls noch nicht geschehen) – vor dem Lesen der Schalter aufrufen</summary>
        Task LadeEinstellungenAsync();
        bool AlarmeUebernehmen { get; }
        bool StatusAnzeigen { get; }
        bool FotosSenden { get; }

        /// <summary>Status der eigenen Divera-Einheit (aus der letzten Abfrage) – fuer die Zuordnung in den Einstellungen</summary>
        IReadOnlyList<DiveraStatusDefinition> StatusDefinitionen { get; }

        /// <summary>Foto in den Messenger-Chat des Divera-Alarms senden (Zusatzmodule Messenger + Cloud)</summary>
        Task<DiveraFotoErgebnis> SendeFotoAnEinsatzChatAsync(int alarmId, byte[] bild, string dateiname, string contentType, string? text);

        /// <summary>Kanal-Pruefung: zeigt, wie Divera den Chat eines Einsatzes liefert (nur Feldnamen und IDs)</summary>
        Task<IReadOnlyList<string>> PruefeEinsatzChatAsync();
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

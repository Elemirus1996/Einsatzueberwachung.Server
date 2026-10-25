// Service-Interface für GPS-Halsband Live-Tracking
// Verwaltet Halsbänder, Zuordnungen zu Teams und Positionsdaten

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Interfaces
{
    public interface ICollarTrackingService
    {
        /// <summary>Alle bekannten Halsbänder</summary>
        IReadOnlyList<Collar> Collars { get; }

        /// <summary>Wird ausgelöst wenn eine neue GPS-Position empfangen wird</summary>
        event Action<string, CollarLocation>? CollarLocationReceived;

        /// <summary>Wird ausgelöst wenn ein Hund sein Suchgebiet verlässt</summary>
        event Action<string, string, CollarLocation>? OutOfBoundsDetected;
        /// <summary>Wird auslöst wenn der Positionsverlauf eines Halsbands gelöscht wird</summary>
        event Action<string>? CollarHistoryCleared;

        /// <summary>Wird ausgelöst wenn ein Team-Track als Snapshot gesichert wurde (nach Stopp)</summary>
        event Action<TeamTrackSnapshot>? TrackSnapshotSaved;

        /// <summary>Benachrichtigt Listener über einen neu gespeicherten Track-Snapshot</summary>
        void NotifySnapshotSaved(TeamTrackSnapshot snapshot);
        /// <summary>Neue GPS-Position von der externen Software empfangen</summary>
        Task<CollarLocation> ReceiveLocationAsync(string collarId, string collarName, double latitude, double longitude, int? batteryLevel = null, DateTime? timestampUtc = null);

        /// <summary>Halsband einem Team zuordnen</summary>
        Task AssignCollarToTeamAsync(string collarId, string teamId);

        /// <summary>Halsband-Zuordnung von einem Team entfernen</summary>
        Task UnassignCollarAsync(string collarId);

        /// <summary>Alle nicht zugewiesenen Halsbänder</summary>
        IReadOnlyList<Collar> GetAvailableCollars();

        /// <summary>Positionsverlauf eines Halsbands abrufen</summary>
        IReadOnlyList<CollarLocation> GetLocationHistory(string collarId);

        /// <summary>Letzte bekannte Position eines Halsbands (auch ausserhalb aktiver Aufzeichnung)</summary>
        CollarLocation? GetLatestLocation(string collarId);

        /// <summary>Positionsverlauf eines Halsbands löschen (z.B. bei neuem Suchstart)</summary>
        void ClearCollarHistory(string collarId);

        /// <summary>
        /// Positionsverlauf eines Halsbands wiederherstellen (z.B. nach Server-Neustart aus Persistenz).
        /// Vorhandene Einträge werden ersetzt, nicht ergänzt.
        /// </summary>
        void SetLocationHistory(string collarId, List<CollarLocation> history);

        /// <summary>Alle Positionsdaten löschen (z.B. bei Einsatz-Ende)</summary>
        void ClearAll();
    }
}

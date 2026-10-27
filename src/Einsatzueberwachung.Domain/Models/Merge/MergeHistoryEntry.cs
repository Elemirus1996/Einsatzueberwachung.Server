// MergeHistoryEntry — Protokolleintrag für eine durchgeführte Zusammenführung
// Gespeichert in EinsatzData.MergeHistory und ArchivedEinsatz.MergeHistory
// Ermöglicht das rückstandslose Rückgängigmachen einer Zusammenführung

using System;
using System.Collections.Generic;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Models.Merge
{
    /// <summary>
    /// Protokolliert eine durchgeführte Import-Zusammenführung.
    /// Speichert die IDs aller hinzugefügten Einträge, damit die Zusammenführung
    /// mit einem einzigen Button-Druck rückgängig gemacht werden kann.
    /// </summary>
    public class MergeHistoryEntry
    {
        /// <summary>Eindeutige ID dieses Merge-Vorgangs.</summary>
        public string MergeId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Zeitpunkt der Zusammenführung.</summary>
        public DateTime MergedAt { get; set; } = Ortszeit.Jetzt;

        /// <summary>Optionaler Label-Name für diesen Import (z. B. "Staffel Nord"). Kann leer sein.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Benutzername (falls verfügbar), der die Zusammenführung durchgeführt hat.</summary>
        public string? MergedBy { get; set; }

        // === IDs aller hinzugefügten Einträge (für Revert) ===

        /// <summary>IDs der hinzugefügten Teams.</summary>
        public List<string> AddedTeamIds { get; set; } = new();

        /// <summary>IDs der hinzugefügten Notizen / Funksprüche.</summary>
        public List<string> AddedNoteIds { get; set; } = new();

        /// <summary>IDs der hinzugefügten Suchgebiete.</summary>
        public List<string> AddedSearchAreaIds { get; set; } = new();

        /// <summary>IDs der hinzugefügten Karten-Marker.</summary>
        public List<string> AddedMapMarkerIds { get; set; } = new();

        /// <summary>IDs der hinzugefügten GPS-Track-Snapshots.</summary>
        public List<string> AddedTrackSnapshotIds { get; set; } = new();

        /// <summary>IDs der hinzugefügten Vermissten-Einträge (Guid als String).</summary>
        public List<string> AddedVermisstenIds { get; set; } = new();

        /// <summary>IDs neu erstellter Personal-Einträge in den Stammdaten (Decision = CreateNew).</summary>
        public List<string> CreatedPersonalIds { get; set; } = new();

        /// <summary>IDs neu erstellter Hunde-Einträge in den Stammdaten (Decision = CreateNew).</summary>
        public List<string> CreatedDogIds { get; set; } = new();

        /// <summary>IDs neu erstellter Drohnen-Einträge in den Stammdaten (Decision = CreateNew).</summary>
        public List<string> CreatedDroneIds { get; set; } = new();

        // === Zusammenfassungszähler (für Anzeige) ===

        public int TeamsAdded { get; set; }
        public int NotesAdded { get; set; }
        public int SearchAreasAdded { get; set; }
        public int MarkersAdded { get; set; }
        public int TracksAdded { get; set; }
        public int PersonalCreated { get; set; }
        public int DogsCreated { get; set; }
        public int DronesCreated { get; set; }

        /// <summary>True, wenn dieser Merge bereits rückgängig gemacht wurde.</summary>
        public bool IsReverted { get; set; } = false;

        /// <summary>Zeitpunkt des Rückgängigmachens (falls durchgeführt).</summary>
        public DateTime? RevertedAt { get; set; }

        /// <summary>Formatierter Zeitstempel für die Anzeige.</summary>
        public string FormattedMergedAt => MergedAt.ToString("dd.MM.yyyy HH:mm");

        /// <summary>Kurzzusammenfassung der hinzugefügten Einträge.</summary>
        public string Summary =>
            $"{TeamsAdded} Teams, {NotesAdded} Notizen, {SearchAreasAdded} Gebiete, {MarkersAdded} Marker";
    }
}

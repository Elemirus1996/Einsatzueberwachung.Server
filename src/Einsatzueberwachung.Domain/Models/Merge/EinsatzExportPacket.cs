// EinsatzExportPacket — Root-Objekt der .einsatz-export.json Datei

using System;
using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models.Merge
{
    /// <summary>
    /// Das Stammobjekt einer exportierten Einsatz-Datei.
    /// Enthält alle Stamm- und Einsatzdaten, die zusammengeführt werden sollen.
    /// </summary>
    public class EinsatzExportPacket
    {
        /// <summary>Schema-Version für Vorwärtskompatibilität (aktuell "1.0").</summary>
        public string SchemaVersion { get; set; } = "1.0";

        /// <summary>
        /// Optionaler Label-Name für dieses Export-Paket (z. B. "Staffel Nord", "Unterabschnitt Mitte").
        /// Wird beim Import vom Benutzer vergeben. Leer wenn kein Name gesetzt wurde.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Zeitpunkt des Exports (UTC).</summary>
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

        /// <summary>EinsatzNummer des Einsatzes, aus dem exportiert wurde.</summary>
        public string EinsatzNummer { get; set; } = string.Empty;

        // === Stammdaten ===
        public List<PersonalEntry> Personal { get; set; } = new();
        public List<DogEntry> Dogs { get; set; } = new();
        public List<DroneEntry> Drones { get; set; } = new();

        // === Einsatzdaten ===
        public List<Team> Teams { get; set; } = new();
        public List<GlobalNotesEntry> Notes { get; set; } = new();
        public List<SearchArea> SearchAreas { get; set; } = new();
        public List<MapMarker> MapMarkers { get; set; } = new();
        public List<TeamTrackSnapshot> TrackSnapshots { get; set; } = new();
        public EinsatzKontaktDaten? Einsatzkontakte { get; set; }
        public List<VermisstenInfo> Vermisste { get; set; } = new();
    }
}

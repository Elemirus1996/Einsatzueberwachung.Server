// Quelle: WPF-Projekt Models/EinsatzData.cs
// Zentrale Einsatzdaten (Einsatzleiter, Ort, Teams, Funksprüche, Suchgebiete)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;

namespace Einsatzueberwachung.Domain.Models
{
    public class EinsatzData
    {
        public string Einsatzleiter { get; set; }
        public string Fuehrungsassistent { get; set; }
        public string Alarmiert { get; set; }
        public string Stichwort { get; set; }
        public string Einsatzort { get; set; }
        public string MapAddress { get; set; }
        public string ExportPfad { get; set; }
        public bool IstEinsatz { get; set; }
        public EinsatzSzenarioType Szenario { get; set; } = EinsatzSzenarioType.Unbestimmt;
        public int AnzahlTeams { get; set; }
        public DateTime EinsatzDatum { get; set; }
        public DateTime? EinsatzEnde { get; set; }

        public string EinsatzNummer { get; set; }
        public string StaffelName { get; set; }
        public string StaffelAdresse { get; set; }
        public string StaffelTelefon { get; set; }
        public string StaffelEmail { get; set; }
        public string StaffelLogoPfad { get; set; }
        public DateTime? AlarmierungsZeit { get; set; }

        public List<GlobalNotesEntry> GlobalNotesEntries { get; set; }
        public List<SearchArea> SearchAreas { get; set; }
        public List<MapMarker> MapMarkers { get; set; }
        public List<Team> Teams { get; set; }

        public (double Latitude, double Longitude)? ElwPosition { get; set; }

        // Alle gespeicherten GPS-Tracks des Einsatzes (Legacy – bleibt für Rückwärtskompatibilität)
        public List<TeamTrackSnapshot> TrackSnapshots { get; set; }

        /// <summary>
        /// Abgeschlossene Suchen (gruppiert). Max. 1 Hund- + 1 Mensch-Track pro Suche.
        /// Ersetzt die flache <see cref="TrackSnapshots"/>-Liste; wird beim Laden ggf. automatisch migriert.
        /// </summary>
        public List<CompletedSearch> CompletedSearches { get; set; }

        // Protokoll aller Import-Zusammenführungen für diesen Einsatz
        public List<MergeHistoryEntry> MergeHistory { get; set; }

        // Liste aller Vermissten (Mantrailer i.d.R. 1, Fläche/Trümmer auch mehrere).
        public List<VermisstenInfo> Vermisste { get; set; } = new();

        /// <summary>
        /// Legacy-Property: Beim Deserialisieren alter Snapshots/Archive (vor Multi-Vermissten-Migration)
        /// wird das Single-Objekt automatisch in <see cref="Vermisste"/> übernommen. Nicht mehr für neue Schreiboperationen verwenden.
        /// </summary>
        [JsonInclude]
        public VermisstenInfo? VermisstenInfo
        {
            get => Vermisste?.FirstOrDefault();
            set
            {
                if (value is null) return;
                Vermisste ??= new List<VermisstenInfo>();
                if (!Vermisste.Any(v => v.Id == value.Id))
                    Vermisste.Add(value);
            }
        }

        // EL-interne Notizen (nur im EL-Dashboard sichtbar)
        public List<ElNotizEntry> ElNotizen { get; set; }

        // Trümmer-Lagekarten (pixel-basiert, ohne GPS — nur bei Szenario.Truemmer relevant)
        public List<TruemmerKarte> TruemmerKarten { get; set; } = new();
        public List<TruemmerArea> TruemmerAreas { get; set; } = new();
        
        // Koordinaten fuer Wetter-Abfrage
        public double? ElwLatitude => ElwPosition?.Latitude;
        public double? ElwLongitude => ElwPosition?.Longitude;

        public EinsatzData()
        {
            Einsatzleiter = string.Empty;
            Fuehrungsassistent = string.Empty;
            Alarmiert = string.Empty;
            Stichwort = string.Empty;
            Einsatzort = string.Empty;
            MapAddress = string.Empty;
            ExportPfad = string.Empty;
            IstEinsatz = true;
            Szenario = EinsatzSzenarioType.Unbestimmt;
            AnzahlTeams = 1;
            EinsatzDatum = DateTime.Now;
            EinsatzEnde = null;
            EinsatzNummer = string.Empty;
            StaffelName = string.Empty;
            StaffelAdresse = string.Empty;
            StaffelTelefon = string.Empty;
            StaffelEmail = string.Empty;
            StaffelLogoPfad = string.Empty;
            AlarmierungsZeit = null;
            GlobalNotesEntries = new List<GlobalNotesEntry>();
            SearchAreas = new List<SearchArea>();
            MapMarkers = new List<MapMarker>();
            Teams = new List<Team>();
            ElwPosition = null;
            TrackSnapshots = new List<TeamTrackSnapshot>();
            CompletedSearches = new List<CompletedSearch>();
            MergeHistory = new List<MergeHistoryEntry>();
            Vermisste = new List<VermisstenInfo>();
            ElNotizen = new List<ElNotizEntry>();
            TruemmerKarten = new List<TruemmerKarte>();
            TruemmerAreas = new List<TruemmerArea>();
        }

        public string EinsatzTyp => IstEinsatz ? "Einsatz" : "Übung";

        /// <summary>
        /// Berechnet die Dauer des Einsatzes zwischen Alarmierung und Ende (oder Jetzt, wenn noch laufend)
        /// </summary>
        public TimeSpan? Dauer
        {
            get
            {
                if (!AlarmierungsZeit.HasValue)
                    return null;

                var endTime = EinsatzEnde ?? DateTime.Now;
                return endTime - AlarmierungsZeit.Value;
            }
        }

        /// <summary>
        /// Formatierte Darstellung der Einsatzdauer (z.B. "2h 45min")
        /// </summary>
        public string DauerFormatiert
        {
            get
            {
                if (!Dauer.HasValue)
                    return "-- : --";

                var d = Dauer.Value;
                return $"{(int)d.TotalHours}h {d.Minutes}min";
            }
        }
    }
}

// Archivierter Einsatz - Speichert abgeschlossene Einsaetze fuer Historien-Ansicht

using System;
using System.Collections.Generic;
using System.Linq;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>
    /// Repraesentiert einen archivierten (abgeschlossenen) Einsatz
    /// </summary>
    public class ArchivedEinsatz
    {
        /// <summary>
        /// Eindeutige ID des archivierten Einsatzes
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Zeitpunkt der Archivierung
        /// </summary>
        public DateTime ArchivedAt { get; set; } = Ortszeit.Jetzt;

        // === Einsatz-Grunddaten ===
        public string Einsatzleiter { get; set; } = string.Empty;
        public string Fuehrungsassistent { get; set; } = string.Empty;
        public string Alarmiert { get; set; } = string.Empty;
        public string Einsatzort { get; set; } = string.Empty;
        public string MapAddress { get; set; } = string.Empty;
        public bool IstEinsatz { get; set; } = true;
        public EinsatzSzenarioType Szenario { get; set; } = EinsatzSzenarioType.Unbestimmt;
        public DateTime EinsatzDatum { get; set; }
        public string EinsatzNummer { get; set; } = string.Empty;
        public string StaffelName { get; set; } = string.Empty;
        public string StaffelAdresse { get; set; } = string.Empty;
        public string StaffelTelefon { get; set; } = string.Empty;
        public string StaffelEmail { get; set; } = string.Empty;
        public string StaffelLogoPfad { get; set; } = string.Empty;
        public DateTime? AlarmierungsZeit { get; set; }

        // === Einsatz-Ende ===
        public DateTime? EinsatzEnde { get; set; }
        public string Ergebnis { get; set; } = string.Empty; // z.B. "Person gefunden", "Erfolglos", "Abgebrochen"
        public string Bemerkungen { get; set; } = string.Empty;

        // === Teams und Statistiken ===
        public int AnzahlTeams { get; set; }
        public int AnzahlPersonal { get; set; }
        public int AnzahlHunde { get; set; }
        public int AnzahlDrohnen { get; set; }
        public int AnzahlRessourcen { get; set; }
        public List<string> PersonalNamen { get; set; } = new();
        public List<string> HundeNamen { get; set; } = new();
        public List<string> DrohnenNamen { get; set; } = new();
        public List<ArchivedTeam> Teams { get; set; } = new();

        // === Notizen und Bereiche ===
        public List<GlobalNotesEntry> GlobalNotesEntries { get; set; } = new();
        public List<SearchArea> SearchAreas { get; set; } = new();

        // === Vermisste Personen ===
        public List<VermisstenInfo> Vermisste { get; set; } = new();

        // === Kartendaten ===
        public (double Latitude, double Longitude)? ElwPosition { get; set; }

        /// <summary>Kartenpunkte, nur wenn beim Einsatzende „Kartenpunkte im Bericht“ gewaehlt wurde; Loeschfrist wie Notizen</summary>
        public List<MapMarker> MapMarkers { get; set; } = new();

        // === GPS-Tracks ===
        public List<TeamTrackSnapshot> TrackSnapshots { get; set; } = new();

        /// <summary>
        /// Abgeschlossene Suchen (gruppiert). Max. 1 Hund- + 1 Mensch-Track pro Suche.
        /// Kann null sein bei alten archivierten Einsätzen – Fallback auf <see cref="TrackSnapshots"/>.
        /// </summary>
        public List<CompletedSearch>? CompletedSearches { get; set; }

        /// <summary>Nach Loeschfrist entfernte Datenarten (Loeschfristen.Laufwege usw.) mit Zeitpunkt (#151)</summary>
        public Dictionary<string, DateTime>? Geloescht { get; set; }

        // === Zusammenführungs-Protokoll ===
        /// <summary>Zeitpunkt der letzten Import-Zusammenführung (null = noch nie zusammengeführt).</summary>
        public DateTime? LastMergedAt { get; set; }

        /// <summary>
        /// Protokoll aller Import-Zusammenführungen für diesen archivierten Einsatz.
        /// Ermöglicht das rückstandslose Rückgängigmachen einzelner Zusammenführungen.
        /// </summary>
        public List<MergeHistoryEntry> MergeHistory { get; set; } = new();

        // === Berechnete Eigenschaften ===
        public string EinsatzTyp => IstEinsatz ? "Einsatz" : "Uebung";
        
        public TimeSpan? Dauer => EinsatzEnde.HasValue && AlarmierungsZeit.HasValue 
            ? EinsatzEnde.Value - AlarmierungsZeit.Value 
            : null;

        public string DauerFormatiert => Dauer.HasValue 
            ? $"{(int)Dauer.Value.TotalHours}h {Dauer.Value.Minutes}min" 
            : "Unbekannt";

        /// <summary>
        /// Erstellt ein ArchivedEinsatz aus aktuellen EinsatzData
        /// </summary>
        public static ArchivedEinsatz FromEinsatzData(EinsatzData data, string ergebnis = "", string bemerkungen = "", DateTime? now = null)
        {
            var _now = now ?? Ortszeit.Jetzt;
            var archived = new ArchivedEinsatz
            {
                ArchivedAt = _now,
                Einsatzleiter = data.Einsatzleiter,
                Fuehrungsassistent = data.Fuehrungsassistent,
                Alarmiert = data.Alarmiert,
                Einsatzort = data.Einsatzort,
                MapAddress = data.MapAddress,
                IstEinsatz = data.IstEinsatz,
                Szenario = data.Szenario,
                EinsatzDatum = data.EinsatzDatum,
                EinsatzNummer = data.EinsatzNummer,
                StaffelName = data.StaffelName,
                StaffelAdresse = data.StaffelAdresse,
                StaffelTelefon = data.StaffelTelefon,
                StaffelEmail = data.StaffelEmail,
                StaffelLogoPfad = data.StaffelLogoPfad,
                AlarmierungsZeit = data.AlarmierungsZeit,
                EinsatzEnde = data.EinsatzEnde ?? _now,
                Ergebnis = ergebnis,
                Bemerkungen = bemerkungen,
                AnzahlTeams = data.Teams?.Count ?? 0,
                GlobalNotesEntries = data.GlobalNotesEntries ?? new List<GlobalNotesEntry>(),
                SearchAreas = data.SearchAreas ?? new List<SearchArea>(),
                Vermisste = data.Vermisste?.ToList() ?? new List<VermisstenInfo>(),
                ElwPosition = data.ElwPosition,
                MapMarkers = data.MapMarkers?.ToList() ?? new List<MapMarker>(),
                TrackSnapshots = data.TrackSnapshots?.ToList() ?? new(),
                CompletedSearches = data.CompletedSearches?.ToList() ?? new()
            };

            var personalNamen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hundeNamen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var drohnenNamen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Teams archivieren
            if (data.Teams != null)
            {
                foreach (var team in data.Teams)
                {
                    archived.Teams.Add(ArchivedTeam.FromTeam(team));

                    if (!string.IsNullOrWhiteSpace(team.HundefuehrerName))
                    {
                        personalNamen.Add(team.HundefuehrerName.Trim());
                    }

                    foreach (var helferName in team.HelferNames)
                    {
                        if (!string.IsNullOrWhiteSpace(helferName))
                            personalNamen.Add(helferName.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(team.DogName))
                    {
                        hundeNamen.Add(team.DogName.Trim());
                    }

                    if (team.IsDroneTeam && !string.IsNullOrWhiteSpace(team.DroneType))
                    {
                        drohnenNamen.Add(team.DroneType.Trim());
                    }
                }
            }

            archived.PersonalNamen = personalNamen.OrderBy(name => name).ToList();
            archived.HundeNamen = hundeNamen.OrderBy(name => name).ToList();
            archived.DrohnenNamen = drohnenNamen.OrderBy(name => name).ToList();

            archived.AnzahlPersonal = archived.PersonalNamen.Count;
            archived.AnzahlHunde = archived.HundeNamen.Count;
            archived.AnzahlDrohnen = archived.DrohnenNamen.Count;
            archived.AnzahlRessourcen = archived.AnzahlPersonal + archived.AnzahlHunde + archived.AnzahlDrohnen;

            return archived;
        }
    }

    /// <summary>
    /// Archiviertes Team mit minimalen Informationen
    /// </summary>
    public class ArchivedTeam
    {
        public string TeamId { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public string Funkrufname { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<string> MemberNames { get; set; } = new();
        public string DogName { get; set; } = string.Empty;
        public string DroneName { get; set; } = string.Empty;
        public DateTime? AusrueckZeit { get; set; }
        public DateTime? EinrueckZeit { get; set; }
        public List<TeamTrackSnapshot> TrackSnapshots { get; set; } = new();

        public static ArchivedTeam FromTeam(Team team)
        {
            var archived = new ArchivedTeam
            {
                TeamId = team.TeamId,
                TeamName = team.TeamName,
                Funkrufname = team.TeamName, // TeamName wird als Funkrufname verwendet
                Status = team.IsRunning ? "Im Einsatz" : "Beendet",
                DogName = team.DogName ?? string.Empty,
                DroneName = team.IsDroneTeam ? team.DroneType ?? string.Empty : string.Empty,
                TrackSnapshots = team.TrackSnapshots?.ToList() ?? new()
            };

            // Personal hinzufuegen
            if (!string.IsNullOrWhiteSpace(team.HundefuehrerName))
            {
                archived.MemberNames.Add(team.HundefuehrerName);
            }
            foreach (var helferName in team.HelferNames)
            {
                if (!string.IsNullOrWhiteSpace(helferName))
                    archived.MemberNames.Add(helferName);
            }

            return archived;
        }
    }
}

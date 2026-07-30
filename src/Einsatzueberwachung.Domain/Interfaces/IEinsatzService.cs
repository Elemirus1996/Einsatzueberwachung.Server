// Service-Interface für Einsatz-Verwaltung (Teams, Timer, Funksprüche)
// Quelle: Abgeleitet von WPF ViewModels/MainViewModel.cs und Models/Team.cs

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Interfaces
{
    public interface IEinsatzService
    {
        EinsatzData CurrentEinsatz { get; }
        List<Team> Teams { get; }
        List<GlobalNotesEntry> GlobalNotes { get; }
        IReadOnlyDictionary<string, TeamPhoneLocation> PhoneLocations { get; }

        event Action? EinsatzChanged;
        event Action<Team>? TeamAdded;
        event Action<Team>? TeamRemoved;
        event Action<Team>? TeamUpdated;
        event Action<GlobalNotesEntry>? NoteAdded;
        event Action<Team, bool>? TeamWarningTriggered;
        event Action? VermisstenInfoChanged;
        event Action<VermisstenInfo>? VermisstenAdded;
        event Action<VermisstenInfo>? VermisstenUpdated;
        event Action<Guid>? VermisstenRemoved;
        event Action? SzenarioChanged;
        event Action<TruemmerKarte>? TruemmerKarteAdded;
        event Action<Guid>? TruemmerKarteRemoved;
        event Action<TruemmerArea>? TruemmerAreaUpserted;
        event Action<Guid>? TruemmerAreaRemoved;
        event Action? ElNotizAdded;
        event Action<string, string, TeamPhoneLocation>? TeamPhoneLocationChanged;

        /// <summary>
        /// Wird ausgelöst wenn ein neuer GPS-Punkt zum Telefon-Track eines aktiv laufenden Teams hinzugefügt wird.
        /// Parameter: teamId, teamName, location
        /// </summary>
        event Action<string, string, TeamPhoneLocation>? TeamPhoneTrackPointAdded;

        event Action<Team>? DogPauseStarted;

        Task StartEinsatzAsync(EinsatzData einsatzData);
        Task UpdateEinsatzAsync(EinsatzData einsatzData);
        Task EndEinsatzAsync();

        Task<Team> AddTeamAsync(Team team);
        Task RemoveTeamAsync(string teamId);
        Task UpdateTeamAsync(Team team);
        Task<Team?> GetTeamByIdAsync(string teamId);

        Task StartTeamTimerAsync(string teamId);
        Task PauseTeamTimerAsync(string teamId);
        Task StopTeamTimerAsync(string teamId);
        Task ResetTeamTimerAsync(string teamId);

        Task AddGlobalNoteAsync(string text, Models.Enums.GlobalNotesEntryType type = Models.Enums.GlobalNotesEntryType.Manual, string teamId = "");
        Task<List<GlobalNotesEntry>> GetFilteredNotesAsync(string? teamId = null);
        
        // Erweiterte Notiz-Funktionen: Bearbeitung, Antworten, Herkunft
        Task<GlobalNotesEntry> AddGlobalNoteWithSourceAsync(string text, string sourceTeamId, string sourceTeamName, string sourceType, Models.Enums.GlobalNotesEntryType type = Models.Enums.GlobalNotesEntryType.Manual, string createdBy = "System");
        Task<GlobalNotesEntry> UpdateGlobalNoteAsync(string noteId, string newText, string updatedBy = "System");
        Task<GlobalNotesEntry?> GetGlobalNoteByIdAsync(string noteId);
        Task<List<GlobalNotesHistory>> GetNoteHistoryAsync(string noteId);
        
        // Antworten/Replies
        Task<GlobalNotesReply> AddReplyToNoteAsync(string noteId, string text, string sourceTeamId, string sourceTeamName, string createdBy = "System");
        Task<GlobalNotesReply> UpdateReplyAsync(string replyId, string newText, string updatedBy = "System");
        Task DeleteReplyAsync(string replyId);
        Task<List<GlobalNotesReply>> GetRepliesForNoteAsync(string noteId);

        Task<SearchArea> AddSearchAreaAsync(SearchArea area);
        Task UpdateSearchAreaAsync(SearchArea area);
        Task DeleteSearchAreaAsync(string areaId);
        Task AssignTeamToSearchAreaAsync(string areaId, string teamId);

        /// <summary>
        /// Übernimmt einen bestehenden Notiz-Eintrag unverändert (Id/Timestamp/Source bleiben),
        /// pflegt beide internen Listen und feuert das <see cref="NoteAdded"/>-Event.
        /// Verwendung: Merge-Import.
        /// </summary>
        Task ImportGlobalNoteAsync(GlobalNotesEntry note);

        /// <summary>
        /// Entfernt eine Notiz anhand ihrer ID (für administrative Revert-Operationen).
        /// </summary>
        Task RemoveGlobalNoteAsync(string noteId);

        Task SetElwPositionAsync(double latitude, double longitude);
        Task ClearElwPositionAsync();

        // Koordinaten-Marker (Punkte auf der Karte)
        Task<MapMarker> AddMapMarkerAsync(MapMarker marker);
        Task<MapMarker?> UpdateMapMarkerAsync(string markerId, string? label = null, double? latitude = null, double? longitude = null);
        Task RemoveMapMarkerAsync(string markerId);
        Task<List<MapMarker>> GetMapMarkersAsync();

        EinsatzRuntimeSnapshot ExportRuntimeSnapshot();
        Task ImportRuntimeSnapshotAsync(EinsatzRuntimeSnapshot snapshot);
        
        /// <summary>
        /// Gibt den aktiven Pausendatensatz für einen Hund zurück, oder null falls keine Pause läuft.
        /// </summary>
        DogPauseRecord? GetDogPause(string dogId);

        /// <summary>
        /// Gibt an, ob der Hund aktuell in einem laufenden (nicht gestoppten) Team eingesetzt ist.
        /// </summary>
        bool IsDogRunning(string dogId);

        /// <summary>
        /// Fügt einen manuell importierten oder extern erzeugten Track-Snapshot zum aktuellen Einsatz hinzu.
        /// Erzeugt implizit eine neue <see cref="CompletedSearch"/> (Legacy-Wrapper).
        /// </summary>
        Task AddTrackSnapshotAsync(TeamTrackSnapshot snapshot);

        event Action<TeamTrackSnapshot>? TrackSnapshotAdded;

        /// <summary>
        /// Legt eine neue abgeschlossene Suchepisode für ein Team an.
        /// </summary>
        Task<CompletedSearch> CreateCompletedSearchAsync(string teamId, DateTime start, DateTime end, string? searchAreaId = null);

        /// <summary>
        /// Fügt einen Track-Snapshot zu einer bestehenden abgeschlossenen Suche hinzu.
        /// Wirft <see cref="InvalidOperationException"/> wenn der TrackType bereits vorhanden ist.
        /// </summary>
        Task AddTrackToCompletedSearchAsync(string completedSearchId, TeamTrackSnapshot snapshot);

        event Action<CompletedSearch>? CompletedSearchUpdated;

        Task UpdateVermisstenInfoAsync(VermisstenInfo info);
        Task UpsertVermisstenAsync(VermisstenInfo info);
        Task RemoveVermisstenAsync(Guid id);
        Task UpdateSzenarioAsync(Models.Enums.EinsatzSzenarioType szenario);

        Task AddTruemmerKarteAsync(TruemmerKarte karte);
        Task RemoveTruemmerKarteAsync(Guid id);
        Task UpsertTruemmerAreaAsync(TruemmerArea area);
        Task RemoveTruemmerAreaAsync(Guid id);
        Task AddElNotizAsync(string text, string prefix = "");
        Task DeleteElNotizAsync(string notizId);

        /// <summary>
        /// Setzt den aktuellen Einsatz zurueck (loescht Teams, Notizen, etc.)
        /// </summary>
        void ResetEinsatz();

        Task UpdateTeamPhoneLocationAsync(string teamId, double lat, double lng, double? accuracy = null);

        /// <summary>
        /// Gibt den aufgezeichneten GPS-Pfad eines laufenden Teams zurück (Telefon-Tracking).
        /// </summary>
        IReadOnlyList<TeamPhoneLocation> GetPhoneTrackHistory(string teamId);

        /// <summary>
        /// Löscht den aufgezeichneten GPS-Pfad eines Teams (z.B. beim Timer-Start oder nach Snapshot-Sicherung).
        /// </summary>
        void ClearPhoneTrackHistory(string teamId);

        /// <summary>
        /// Stellt den aufgezeichneten GPS-Pfad eines Teams wieder her (z.B. nach Server-Neustart aus Persistenz).
        /// Vorhandene Einträge werden ersetzt, nicht ergänzt.
        /// </summary>
        void SetPhoneTrackHistory(string teamId, List<TeamPhoneLocation> history);

        /// <summary>
        /// Gibt alle vorhandenen Telefon-Track-Verläufe zurück (pro Team-ID).
        /// Wird für die Persistenz verwendet.
        /// </summary>
        IReadOnlyDictionary<string, IReadOnlyList<TeamPhoneLocation>> GetAllPhoneTrackHistories();
    }
}

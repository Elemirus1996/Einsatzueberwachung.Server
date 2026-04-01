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

        event Action? EinsatzChanged;
        event Action<Team>? TeamAdded;
        event Action<Team>? TeamRemoved;
        event Action<Team>? TeamUpdated;
        event Action<GlobalNotesEntry>? NoteAdded;
        event Action<Team, bool>? TeamWarningTriggered;

        Task StartEinsatzAsync(EinsatzData einsatzData);
        Task EndEinsatzAsync();

        Task<Team> AddTeamAsync(Team team);
        Task RemoveTeamAsync(string teamId);
        Task UpdateTeamAsync(Team team);
        Task<Team?> GetTeamByIdAsync(string teamId);

        Task StartTeamTimerAsync(string teamId);
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

        EinsatzRuntimeSnapshot ExportRuntimeSnapshot();
        Task ImportRuntimeSnapshotAsync(EinsatzRuntimeSnapshot snapshot);
        
        /// <summary>
        /// Setzt den aktuellen Einsatz zurueck (loescht Teams, Notizen, etc.)
        /// </summary>
        void ResetEinsatz();
    }
}

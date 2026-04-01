// Implementierung des Einsatz-Service für laufenden Einsatz
// Quelle: Abgeleitet von WPF ViewModels/MainViewModel.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Services
{
    public class EinsatzService : IEinsatzService
    {
        private readonly ISettingsService? _settingsService;
        private EinsatzData _currentEinsatz;
        private readonly List<Team> _teams;
        private readonly List<GlobalNotesEntry> _globalNotes;
        private readonly List<GlobalNotesHistory> _noteHistory;

        public EinsatzData CurrentEinsatz => _currentEinsatz;
        public List<Team> Teams => _teams;
        public List<GlobalNotesEntry> GlobalNotes => _globalNotes;

        public event Action? EinsatzChanged;
        public event Action<Team>? TeamAdded;
        public event Action<Team>? TeamRemoved;
        public event Action<Team>? TeamUpdated;
        public event Action<GlobalNotesEntry>? NoteAdded;
        public event Action<Team, bool>? TeamWarningTriggered;

        public EinsatzService(ISettingsService? settingsService = null)
        {
            _settingsService = settingsService;
            _currentEinsatz = new EinsatzData();
            _teams = new List<Team>();
            _globalNotes = new List<GlobalNotesEntry>();
            _noteHistory = new List<GlobalNotesHistory>();

            EnsureCurrentEinsatzTeamReference();
        }

        public async Task StartEinsatzAsync(EinsatzData einsatzData)
        {
            await ApplyStaffelFallbackAsync(einsatzData);

            _currentEinsatz = einsatzData;
            _teams.Clear();
            _globalNotes.Clear();
            _noteHistory.Clear();
            EnsureCurrentEinsatzTeamReference();

            var startNote = new GlobalNotesEntry
            {
                Text = $"Einsatz gestartet: {einsatzData.EinsatzTyp} - {einsatzData.Einsatzort}",
                Type = GlobalNotesEntryType.EinsatzUpdate,
                Timestamp = DateTime.Now
            };
            _globalNotes.Add(startNote);

            EinsatzChanged?.Invoke();
            NoteAdded?.Invoke(startNote);

            return;
        }

        private async Task ApplyStaffelFallbackAsync(EinsatzData einsatzData)
        {
            if (_settingsService is null)
            {
                return;
            }

            var settings = await _settingsService.GetStaffelSettingsAsync();

            if (string.IsNullOrWhiteSpace(einsatzData.StaffelName))
            {
                einsatzData.StaffelName = settings.StaffelName;
            }

            if (string.IsNullOrWhiteSpace(einsatzData.StaffelAdresse))
            {
                einsatzData.StaffelAdresse = settings.StaffelAdresse;
            }

            if (string.IsNullOrWhiteSpace(einsatzData.StaffelTelefon))
            {
                einsatzData.StaffelTelefon = settings.StaffelTelefon;
            }

            if (string.IsNullOrWhiteSpace(einsatzData.StaffelEmail))
            {
                einsatzData.StaffelEmail = settings.StaffelEmail;
            }

            if (string.IsNullOrWhiteSpace(einsatzData.StaffelLogoPfad))
            {
                einsatzData.StaffelLogoPfad = settings.StaffelLogoPfad;
            }
        }

        public Task EndEinsatzAsync()
        {
            foreach (var team in _teams)
            {
                team.StopTimer();
            }

            var endNote = new GlobalNotesEntry
            {
                Text = $"Einsatz beendet",
                Type = GlobalNotesEntryType.EinsatzUpdate,
                Timestamp = DateTime.Now
            };
            _globalNotes.Add(endNote);
            NoteAdded?.Invoke(endNote);

            return Task.CompletedTask;
        }

        public Task<Team> AddTeamAsync(Team team)
        {
            _teams.Add(team);

            team.TimerStarted += Team_TimerStarted;
            team.TimerStopped += Team_TimerStopped;
            team.TimerReset += Team_TimerReset;
            team.WarningTriggered += Team_WarningTriggered;

            TeamAdded?.Invoke(team);

            return Task.FromResult(team);
        }

        public Task RemoveTeamAsync(string teamId)
        {
            var team = _teams.FirstOrDefault(t => t.TeamId == teamId);
            if (team != null)
            {
                team.StopTimer();
                team.Dispose();
                _teams.Remove(team);
                TeamRemoved?.Invoke(team);
            }

            return Task.CompletedTask;
        }

        public Task UpdateTeamAsync(Team team)
        {
            var existing = _teams.FirstOrDefault(t => t.TeamId == team.TeamId);
            if (existing != null)
            {
                CopyMutableTeamFields(existing, team);
                TeamUpdated?.Invoke(existing);
            }

            return Task.CompletedTask;
        }

        public Task<Team?> GetTeamByIdAsync(string teamId)
        {
            var team = _teams.FirstOrDefault(t => t.TeamId == teamId);
            return Task.FromResult(team);
        }

        public async Task StartTeamTimerAsync(string teamId)
        {
            var team = await GetTeamByIdAsync(teamId);
            team?.StartTimer();
        }

        public async Task StopTeamTimerAsync(string teamId)
        {
            var team = await GetTeamByIdAsync(teamId);
            team?.StopTimer();
        }

        public async Task ResetTeamTimerAsync(string teamId)
        {
            var team = await GetTeamByIdAsync(teamId);
            team?.ResetTimer();
        }

        public Task AddGlobalNoteAsync(string text, GlobalNotesEntryType type = GlobalNotesEntryType.Manual, string teamId = "")
        {
            var note = new GlobalNotesEntry
            {
                Text = text,
                Type = type,
                SourceTeamId = teamId,
                Timestamp = DateTime.Now
            };

            if (!string.IsNullOrEmpty(teamId))
            {
                var team = _teams.FirstOrDefault(t => t.TeamId == teamId);
                if (team != null)
                {
                    note.SourceTeamName = team.TeamName;
                }
            }

            _globalNotes.Add(note);
            NoteAdded?.Invoke(note);

            return Task.CompletedTask;
        }

        public Task<List<GlobalNotesEntry>> GetFilteredNotesAsync(string? teamId = null)
        {
            if (string.IsNullOrEmpty(teamId))
            {
                return Task.FromResult(_globalNotes.OrderByDescending(n => n.Timestamp).ToList());
            }

            var filtered = _globalNotes
                .Where(n => string.IsNullOrEmpty(n.SourceTeamId) || n.SourceTeamId == teamId)
                .OrderByDescending(n => n.Timestamp)
                .ToList();

            return Task.FromResult(filtered);
        }

        public Task<SearchArea> AddSearchAreaAsync(SearchArea area)
        {
            _currentEinsatz.SearchAreas.Add(area);
            EinsatzChanged?.Invoke();
            return Task.FromResult(area);
        }

        public Task UpdateSearchAreaAsync(SearchArea area)
        {
            var existing = _currentEinsatz.SearchAreas.FirstOrDefault(a => a.Id == area.Id);
            if (existing != null)
            {
                var index = _currentEinsatz.SearchAreas.IndexOf(existing);
                _currentEinsatz.SearchAreas[index] = area;
                EinsatzChanged?.Invoke();
            }

            return Task.CompletedTask;
        }

        public Task DeleteSearchAreaAsync(string areaId)
        {
            var area = _currentEinsatz.SearchAreas.FirstOrDefault(a => a.Id == areaId);
            if (area != null)
            {
                if (!string.IsNullOrWhiteSpace(area.AssignedTeamId))
                {
                    var assignedTeam = _teams.FirstOrDefault(t => t.TeamId == area.AssignedTeamId);
                    if (assignedTeam != null)
                    {
                        assignedTeam.SearchAreaId = string.Empty;
                        assignedTeam.SearchAreaName = string.Empty;
                        TeamUpdated?.Invoke(assignedTeam);
                    }
                }

                _currentEinsatz.SearchAreas.Remove(area);
                EinsatzChanged?.Invoke();
            }

            return Task.CompletedTask;
        }

        public Task AssignTeamToSearchAreaAsync(string areaId, string teamId)
        {
            var area = _currentEinsatz.SearchAreas.FirstOrDefault(a => a.Id == areaId);
            if (area == null)
            {
                return Task.CompletedTask;
            }

            // Falls das Gebiet bereits einem anderen Team zugeordnet ist, alte Zuordnung entfernen.
            if (!string.IsNullOrWhiteSpace(area.AssignedTeamId) && area.AssignedTeamId != teamId)
            {
                var oldTeam = _teams.FirstOrDefault(t => t.TeamId == area.AssignedTeamId);
                if (oldTeam != null)
                {
                    oldTeam.SearchAreaId = string.Empty;
                    oldTeam.SearchAreaName = string.Empty;
                    TeamUpdated?.Invoke(oldTeam);
                }
            }

            if (string.IsNullOrWhiteSpace(teamId))
            {
                if (!string.IsNullOrWhiteSpace(area.AssignedTeamId))
                {
                    var previousTeam = _teams.FirstOrDefault(t => t.TeamId == area.AssignedTeamId);
                    if (previousTeam != null)
                    {
                        previousTeam.SearchAreaId = string.Empty;
                        previousTeam.SearchAreaName = string.Empty;
                        TeamUpdated?.Invoke(previousTeam);
                    }
                }

                area.AssignedTeamId = string.Empty;
                area.AssignedTeamName = string.Empty;
                EinsatzChanged?.Invoke();
                return Task.CompletedTask;
            }

            var team = _teams.FirstOrDefault(t => t.TeamId == teamId);
            if (team == null)
            {
                return Task.CompletedTask;
            }

            // Ein Team darf nur genau einem Gebiet zugeordnet sein.
            foreach (var otherArea in _currentEinsatz.SearchAreas.Where(a => a.AssignedTeamId == team.TeamId && a.Id != area.Id))
            {
                otherArea.AssignedTeamId = string.Empty;
                otherArea.AssignedTeamName = string.Empty;
            }

            area.AssignedTeamId = teamId;
            area.AssignedTeamName = team.TeamName;
            team.SearchAreaId = areaId;
            team.SearchAreaName = area.Name;
            EinsatzChanged?.Invoke();
            TeamUpdated?.Invoke(team);

            return Task.CompletedTask;
        }

        private void Team_TimerStarted(Team team)
        {
            _ = AddGlobalNoteAsync($"Timer gestartet", GlobalNotesEntryType.TeamStart, team.TeamId);
        }

        private void Team_TimerStopped(Team team)
        {
            _ = AddGlobalNoteAsync($"Timer gestoppt", GlobalNotesEntryType.TeamStop, team.TeamId);
        }

        private void Team_TimerReset(Team team)
        {
            _ = AddGlobalNoteAsync($"Timer zurückgesetzt", GlobalNotesEntryType.TeamReset, team.TeamId);
        }

        private void Team_WarningTriggered(Team team, bool isSecondWarning)
        {
            var warningType = isSecondWarning ? "Zweite" : "Erste";
            _ = AddGlobalNoteAsync($"{warningType} Warnung erreicht!", GlobalNotesEntryType.TeamWarning, team.TeamId);
            
            // Event für UI (damit Blazor die akustische Warnung abspielen kann)
            TeamWarningTriggered?.Invoke(team, isSecondWarning);
        }
        
        // ========================================
        // Erweiterte Notiz-Funktionen
        // ========================================
        
        public Task<GlobalNotesEntry> AddGlobalNoteWithSourceAsync(
            string text, 
            string sourceTeamId, 
            string sourceTeamName, 
            string sourceType, 
            GlobalNotesEntryType type = GlobalNotesEntryType.Manual, 
            string createdBy = "System")
        {
            var note = new GlobalNotesEntry
            {
                Text = text,
                Type = type,
                SourceTeamId = sourceTeamId,
                SourceTeamName = sourceTeamName,
                SourceType = sourceType,
                Timestamp = DateTime.Now,
                CreatedBy = createdBy,
                Replies = new List<GlobalNotesReply>()
            };

            _globalNotes.Add(note);
            _currentEinsatz.GlobalNotesEntries.Add(note);
            NoteAdded?.Invoke(note);

            return Task.FromResult(note);
        }
        
        public Task<GlobalNotesEntry> UpdateGlobalNoteAsync(string noteId, string newText, string updatedBy = "System")
        {
            var note = _globalNotes.FirstOrDefault(n => n.Id == noteId);
            if (note == null)
            {
                throw new InvalidOperationException($"Notiz mit ID {noteId} nicht gefunden");
            }

            // Optional: Historie speichern
            var history = new GlobalNotesHistory
            {
                NoteId = noteId,
                OldText = note.Text,
                NewText = newText,
                ChangedAt = DateTime.Now,
                ChangedBy = updatedBy
            };
            _noteHistory.Add(history);

            note.Text = newText;
            note.UpdatedAt = DateTime.Now;
            note.UpdatedBy = updatedBy;

            EinsatzChanged?.Invoke();
            
            return Task.FromResult(note);
        }
        
        public Task<GlobalNotesEntry?> GetGlobalNoteByIdAsync(string noteId)
        {
            var note = _globalNotes.FirstOrDefault(n => n.Id == noteId);
            return Task.FromResult(note);
        }
        
        public Task<List<GlobalNotesHistory>> GetNoteHistoryAsync(string noteId)
        {
            var history = _noteHistory
                .Where(h => h.NoteId == noteId)
                .OrderByDescending(h => h.ChangedAt)
                .ToList();

            return Task.FromResult(history);
        }
        
        // ========================================
        // Antworten/Replies
        // ========================================
        
        public Task<GlobalNotesReply> AddReplyToNoteAsync(
            string noteId, 
            string text, 
            string sourceTeamId, 
            string sourceTeamName, 
            string createdBy = "System")
        {
            var note = _globalNotes.FirstOrDefault(n => n.Id == noteId);
            if (note == null)
            {
                throw new InvalidOperationException($"Notiz mit ID {noteId} nicht gefunden");
            }

            var reply = new GlobalNotesReply
            {
                NoteId = noteId,
                Text = text,
                SourceTeamId = sourceTeamId,
                SourceTeamName = sourceTeamName,
                Timestamp = DateTime.Now,
                CreatedBy = createdBy
            };

            note.Replies.Add(reply);
            EinsatzChanged?.Invoke();
            
            return Task.FromResult(reply);
        }
        
        public Task<GlobalNotesReply> UpdateReplyAsync(string replyId, string newText, string updatedBy = "System")
        {
            GlobalNotesReply? reply = null;
            
            foreach (var note in _globalNotes)
            {
                reply = note.Replies.FirstOrDefault(r => r.Id == replyId);
                if (reply != null) break;
            }
            
            if (reply == null)
            {
                throw new InvalidOperationException($"Antwort mit ID {replyId} nicht gefunden");
            }

            reply.Text = newText;
            reply.UpdatedAt = DateTime.Now;
            reply.UpdatedBy = updatedBy;

            EinsatzChanged?.Invoke();
            
            return Task.FromResult(reply);
        }
        
        public Task DeleteReplyAsync(string replyId)
        {
            foreach (var note in _globalNotes)
            {
                var reply = note.Replies.FirstOrDefault(r => r.Id == replyId);
                if (reply != null)
                {
                    note.Replies.Remove(reply);
                    EinsatzChanged?.Invoke();
                    break;
                }
            }
            
            return Task.CompletedTask;
        }
        
        public Task<List<GlobalNotesReply>> GetRepliesForNoteAsync(string noteId)
        {
            var note = _globalNotes.FirstOrDefault(n => n.Id == noteId);
            if (note == null)
            {
                return Task.FromResult(new List<GlobalNotesReply>());
            }
            
            return Task.FromResult(note.Replies.OrderBy(r => r.Timestamp).ToList());
        }
        
        public void ResetEinsatz()
        {
            // Alle Timer stoppen
            foreach (var team in _teams)
            {
                team.StopTimer();
                team.TimerStarted -= Team_TimerStarted;
                team.TimerStopped -= Team_TimerStopped;
                team.TimerReset -= Team_TimerReset;
                team.WarningTriggered -= Team_WarningTriggered;
            }
            
            // Teams und Notizen leeren
            _teams.Clear();
            _globalNotes.Clear();
            _noteHistory.Clear();
            
            // Einsatz-Daten zuruecksetzen
            _currentEinsatz = new EinsatzData
            {
                EinsatzDatum = DateTime.Now,
                IstEinsatz = true
            };

            EnsureCurrentEinsatzTeamReference();
            
            EinsatzChanged?.Invoke();
        }

        public EinsatzRuntimeSnapshot ExportRuntimeSnapshot()
        {
            return new EinsatzRuntimeSnapshot
            {
                CurrentEinsatz = _currentEinsatz,
                Teams = _teams.ToList(),
                GlobalNotes = _globalNotes.ToList(),
                NoteHistory = _noteHistory.ToList()
            };
        }

        public Task ImportRuntimeSnapshotAsync(EinsatzRuntimeSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return Task.CompletedTask;
            }

            foreach (var team in _teams)
            {
                team.TimerStarted -= Team_TimerStarted;
                team.TimerStopped -= Team_TimerStopped;
                team.TimerReset -= Team_TimerReset;
                team.WarningTriggered -= Team_WarningTriggered;
            }

            _teams.Clear();
            _globalNotes.Clear();
            _noteHistory.Clear();

            _currentEinsatz = snapshot.CurrentEinsatz ?? new EinsatzData();

            var importedTeams = snapshot.Teams ?? new List<Team>();
            foreach (var team in importedTeams)
            {
                _teams.Add(team);
                team.TimerStarted += Team_TimerStarted;
                team.TimerStopped += Team_TimerStopped;
                team.TimerReset += Team_TimerReset;
                team.WarningTriggered += Team_WarningTriggered;
            }

            if (snapshot.GlobalNotes != null)
            {
                _globalNotes.AddRange(snapshot.GlobalNotes);
            }

            if (snapshot.NoteHistory != null)
            {
                _noteHistory.AddRange(snapshot.NoteHistory);
            }

            if (_currentEinsatz.GlobalNotesEntries == null)
            {
                _currentEinsatz.GlobalNotesEntries = new List<GlobalNotesEntry>();
            }

            _currentEinsatz.GlobalNotesEntries.Clear();
            _currentEinsatz.GlobalNotesEntries.AddRange(_globalNotes);

            EnsureCurrentEinsatzTeamReference();
            EinsatzChanged?.Invoke();

            return Task.CompletedTask;
        }

        private void EnsureCurrentEinsatzTeamReference()
        {
            _currentEinsatz.Teams = _teams;
        }

        private static void CopyMutableTeamFields(Team target, Team source)
        {
            target.TeamName = source.TeamName;
            target.DogName = source.DogName;
            target.DogId = source.DogId;
            target.DogSpecialization = source.DogSpecialization;
            target.HundefuehrerName = source.HundefuehrerName;
            target.HundefuehrerId = source.HundefuehrerId;
            target.HelferName = source.HelferName;
            target.HelferId = source.HelferId;
            target.SearchAreaName = source.SearchAreaName;
            target.SearchAreaId = source.SearchAreaId;
            target.FirstWarningMinutes = source.FirstWarningMinutes;
            target.SecondWarningMinutes = source.SecondWarningMinutes;
            target.Notes = source.Notes;
            target.IsDroneTeam = source.IsDroneTeam;
            target.DroneType = source.DroneType;
            target.DroneId = source.DroneId;
            target.IsSupportTeam = source.IsSupportTeam;
        }
    }
}


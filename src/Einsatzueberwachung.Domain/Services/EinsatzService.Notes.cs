using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzService
    {
        public Task AddGlobalNoteAsync(string text, GlobalNotesEntryType type = GlobalNotesEntryType.Manual, string teamId = "")
        {
            var note = new GlobalNotesEntry
            {
                Text = text,
                Type = type,
                SourceTeamId = teamId,
                Timestamp = _timeService?.Now ?? Ortszeit.Jetzt
            };

            if (!string.IsNullOrWhiteSpace(teamId))
            {
                var team = _teams.FirstOrDefault(t => t.TeamId == teamId);
                if (team != null)
                    note.SourceTeamName = team.TeamName;
            }

            _globalNotes.Add(note);
            _currentEinsatz.GlobalNotesEntries.Add(note);
            NoteAdded?.Invoke(note);
            return Task.CompletedTask;
        }

        public Task<List<GlobalNotesEntry>> GetFilteredNotesAsync(string? teamId = null)
        {
            if (string.IsNullOrWhiteSpace(teamId))
                return Task.FromResult(_globalNotes.OrderByDescending(n => n.Timestamp).ToList());

            var filtered = _globalNotes
                .Where(n => string.IsNullOrWhiteSpace(n.SourceTeamId) || n.SourceTeamId == teamId)
                .OrderByDescending(n => n.Timestamp)
                .ToList();
            return Task.FromResult(filtered);
        }

        public Task ImportGlobalNoteAsync(GlobalNotesEntry note)
        {
            _globalNotes.Add(note);
            _currentEinsatz.GlobalNotesEntries.Add(note);
            NoteAdded?.Invoke(note);
            return Task.CompletedTask;
        }

        public Task RemoveGlobalNoteAsync(string noteId)
        {
            var note = _globalNotes.FirstOrDefault(n => n.Id == noteId);
            if (note != null)
            {
                _globalNotes.Remove(note);
                _currentEinsatz.GlobalNotesEntries.Remove(note);
            }
            return Task.CompletedTask;
        }

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
                Timestamp = _timeService?.Now ?? Ortszeit.Jetzt,
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
            var note = _globalNotes.FirstOrDefault(n => n.Id == noteId)
                ?? throw new InvalidOperationException($"Notiz mit ID {noteId} nicht gefunden");

            _noteHistory.Add(new GlobalNotesHistory
            {
                NoteId = noteId,
                OldText = note.Text,
                NewText = newText,
                ChangedAt = _timeService?.Now ?? Ortszeit.Jetzt,
                ChangedBy = updatedBy
            });

            note.Text = newText;
            note.UpdatedAt = _timeService?.Now ?? Ortszeit.Jetzt;
            note.UpdatedBy = updatedBy;

            EinsatzChanged?.Invoke();
            return Task.FromResult(note);
        }

        public Task<GlobalNotesEntry?> GetGlobalNoteByIdAsync(string noteId)
            => Task.FromResult(_globalNotes.FirstOrDefault(n => n.Id == noteId));

        public Task<List<GlobalNotesHistory>> GetNoteHistoryAsync(string noteId)
        {
            var history = _noteHistory
                .Where(h => h.NoteId == noteId)
                .OrderByDescending(h => h.ChangedAt)
                .ToList();
            return Task.FromResult(history);
        }

        public Task<GlobalNotesReply> AddReplyToNoteAsync(
            string noteId,
            string text,
            string sourceTeamId,
            string sourceTeamName,
            string createdBy = "System")
        {
            var note = _globalNotes.FirstOrDefault(n => n.Id == noteId)
                ?? throw new InvalidOperationException($"Notiz mit ID {noteId} nicht gefunden");

            var reply = new GlobalNotesReply
            {
                NoteId = noteId,
                Text = text,
                SourceTeamId = sourceTeamId,
                SourceTeamName = sourceTeamName,
                Timestamp = _timeService?.Now ?? Ortszeit.Jetzt,
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
                throw new InvalidOperationException($"Antwort mit ID {replyId} nicht gefunden");

            reply.Text = newText;
            reply.UpdatedAt = _timeService?.Now ?? Ortszeit.Jetzt;
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
                return Task.FromResult(new List<GlobalNotesReply>());
            return Task.FromResult(note.Replies.OrderBy(r => r.Timestamp).ToList());
        }

        public Task UpdateVermisstenInfoAsync(VermisstenInfo info)
            => UpsertVermisstenAsync(info);

        public async Task UpsertVermisstenAsync(VermisstenInfo info)
        {
            if (info.Id == Guid.Empty)
                info.Id = Guid.NewGuid();

            _currentEinsatz.Vermisste ??= new List<VermisstenInfo>();

            var isUpdate = _currentEinsatz.Vermisste.Any(v => v.Id == info.Id);
            if (!isUpdate
                && _currentEinsatz.Vermisste.Count > 0
                && !_currentEinsatz.Szenario.AllowsMultipleVermisste())
            {
                throw new InvalidOperationException(
                    $"Im Szenario '{_currentEinsatz.Szenario.GetDisplayName()}' ist nur eine vermisste Person zulässig.");
            }

            info.ZuletztAktualisiert = _timeService?.Now ?? Ortszeit.Jetzt;

            // Beim ersten Anlegen ohne Checkliste das Default-Template für das aktuelle Szenario anhängen.
            if (info.Checkliste is null
                && _masterDataService is not null
                && _currentEinsatz.Szenario != EinsatzSzenarioType.Unbestimmt)
            {
                var template = await _masterDataService.GetDefaultChecklistTemplateAsync(_currentEinsatz.Szenario);
                if (template is not null)
                    info.Checkliste = ChecklistInstance.FromTemplate(template);
            }

            var idx = _currentEinsatz.Vermisste.FindIndex(v => v.Id == info.Id);
            var added = idx < 0;
            if (added)
                _currentEinsatz.Vermisste.Add(info);
            else
                _currentEinsatz.Vermisste[idx] = info;

            VermisstenInfoChanged?.Invoke();
            if (added)
                VermisstenAdded?.Invoke(info);
            else
                VermisstenUpdated?.Invoke(info);
            EinsatzChanged?.Invoke();
        }

        public Task RemoveVermisstenAsync(Guid id)
        {
            if (_currentEinsatz.Vermisste is null)
                return Task.CompletedTask;

            var entry = _currentEinsatz.Vermisste.FirstOrDefault(v => v.Id == id);
            if (entry is null)
                return Task.CompletedTask;

            _currentEinsatz.Vermisste.Remove(entry);
            VermisstenInfoChanged?.Invoke();
            VermisstenRemoved?.Invoke(id);
            EinsatzChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task UpdateSzenarioAsync(EinsatzSzenarioType szenario)
        {
            if (_currentEinsatz.Szenario == szenario)
                return Task.CompletedTask;

            _currentEinsatz.Szenario = szenario;
            SzenarioChanged?.Invoke();
            EinsatzChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task AddElNotizAsync(string text, string prefix = "")
        {
            if (string.IsNullOrWhiteSpace(text))
                return Task.CompletedTask;

            var entry = new ElNotizEntry
            {
                Text = text.Trim(),
                Prefix = prefix,
                Timestamp = _timeService?.Now ?? Ortszeit.Jetzt
            };
            _currentEinsatz.ElNotizen ??= new List<ElNotizEntry>();
            _currentEinsatz.ElNotizen.Insert(0, entry);
            ElNotizAdded?.Invoke();
            return Task.CompletedTask;
        }

        public Task DeleteElNotizAsync(string notizId)
        {
            _currentEinsatz.ElNotizen ??= new List<ElNotizEntry>();
            var entry = _currentEinsatz.ElNotizen.FirstOrDefault(n => n.Id == notizId);
            if (entry != null)
                _currentEinsatz.ElNotizen.Remove(entry);
            return Task.CompletedTask;
        }
    }
}

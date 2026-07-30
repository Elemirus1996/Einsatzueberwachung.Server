using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzService
    {
        public async Task<Team> AddTeamAsync(Team team)
        {
            if (_timeService is not null)
                team.CreatedAt = _timeService.Now;

            if (_settingsService is not null)
            {
                var appSettings = await _settingsService.GetAppSettingsAsync();
                if (appSettings.PauseThresholdMinutes > 0) team.PauseThresholdMinutes = appSettings.PauseThresholdMinutes;
                if (appSettings.PauseMinutesShortRun > 0) team.PauseMinutesShortRun = appSettings.PauseMinutesShortRun;
                if (appSettings.PauseMinutesLongRun > 0) team.PauseMinutesLongRun = appSettings.PauseMinutesLongRun;
            }

            if (!string.IsNullOrWhiteSpace(team.DogId)
                && _dogPauses.TryGetValue(team.DogId, out var dogPause)
                && !dogPause.IsPauseComplete)
            {
                team.SyncPauseFromDog(dogPause.PauseStartTime, dogPause.RunTimeBeforePause, dogPause.RequiredPauseMinutes);
            }

            _teams.Add(team);
            team.TimerStarted += Team_TimerStarted;
            team.TimerStopped += Team_TimerStopped;
            team.TimerReset += Team_TimerReset;
            team.TimerPaused += Team_TimerPaused;
            team.WarningTriggered += Team_WarningTriggered;

            TeamAdded?.Invoke(team);
            return team;
        }

        public Task RemoveTeamAsync(string teamId)
        {
            if (IsEinsatzAktiv())
                throw new InvalidOperationException("Teams koennen waehrend eines laufenden Einsatzes nicht geloescht werden.");

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
            => Task.FromResult(_teams.FirstOrDefault(t => t.TeamId == teamId));

        public async Task StartTeamTimerAsync(string teamId)
        {
            var team = await GetTeamByIdAsync(teamId);
            if (team == null) return;

            if (!string.IsNullOrWhiteSpace(team.DogId))
            {
                var conflict = _teams.FirstOrDefault(t => t.TeamId != teamId && t.DogId == team.DogId && t.IsRunning);
                if (conflict != null)
                    throw new InvalidOperationException($"Hund '{team.DogName}' laeuft bereits in Team '{conflict.TeamName}'. Bitte zuerst dieses Team stoppen.");

                _dogPauses.Remove(team.DogId);
            }

            team.StartTimer(_timeService?.Now ?? DateTime.Now);
            ClearPhoneTrackHistory(teamId);
        }

        public async Task PauseTeamTimerAsync(string teamId)
        {
            var team = await GetTeamByIdAsync(teamId);
            if (team == null) return;

            team.PauseTimer(_timeService?.Now ?? DateTime.Now);
        }

        public async Task StopTeamTimerAsync(string teamId)
        {
            var team = await GetTeamByIdAsync(teamId);
            if (team == null) return;

            team.StopTimer();

            if (team.IsPausing && !string.IsNullOrWhiteSpace(team.DogId) && team.PauseStartTime.HasValue)
            {
                var record = new DogPauseRecord
                {
                    DogId = team.DogId,
                    DogName = team.DogName,
                    PauseStartTime = team.PauseStartTime.Value,
                    RunTimeBeforePause = team.RunTimeBeforePause,
                    RequiredPauseMinutes = team.RequiredPauseMinutes
                };
                _dogPauses[team.DogId] = record;

                foreach (var sibling in _teams.Where(t => t.TeamId != teamId && t.DogId == team.DogId))
                    sibling.SyncPauseFromDog(record.PauseStartTime, record.RunTimeBeforePause, record.RequiredPauseMinutes);

                DogPauseStarted?.Invoke(team);
            }
        }

        public async Task ResetTeamTimerAsync(string teamId)
        {
            var team = await GetTeamByIdAsync(teamId);
            if (team == null) return;

            var dogId = team.DogId;
            team.ResetTimer();

            if (!string.IsNullOrWhiteSpace(dogId))
            {
                _dogPauses.Remove(dogId);
                foreach (var sibling in _teams.Where(t => t.TeamId != teamId && t.DogId == dogId && t.IsPausing).ToList())
                {
                    sibling.SilentReset();
                    TeamUpdated?.Invoke(sibling);
                }
            }
        }

        public DogPauseRecord? GetDogPause(string dogId)
        {
            if (string.IsNullOrWhiteSpace(dogId)) return null;
            return _dogPauses.TryGetValue(dogId, out var record) ? record : null;
        }

        public bool IsDogRunning(string dogId)
        {
            if (string.IsNullOrWhiteSpace(dogId)) return false;
            return _teams.Any(t => t.DogId == dogId && t.IsRunning);
        }

        private void Team_TimerStarted(Team team)
            => _ = AddGlobalNoteAsync("Timer gestartet", GlobalNotesEntryType.TeamStart, team.TeamId);

        private void Team_TimerStopped(Team team)
            => _ = AddGlobalNoteAsync("Timer gestoppt", GlobalNotesEntryType.TeamStop, team.TeamId);

        private void Team_TimerReset(Team team)
            => _ = AddGlobalNoteAsync("Timer zurückgesetzt", GlobalNotesEntryType.TeamReset, team.TeamId);

        private void Team_TimerPaused(Team team)
            => _ = AddGlobalNoteAsync("Timer pausiert", GlobalNotesEntryType.TeamPause, team.TeamId);

        private void Team_WarningTriggered(Team team, bool isSecondWarning)
        {
            var warningType = isSecondWarning ? "Zweite" : "Erste";
            _ = AddGlobalNoteAsync($"{warningType} Warnung erreicht!", GlobalNotesEntryType.TeamWarning, team.TeamId);
            TeamWarningTriggered?.Invoke(team, isSecondWarning);
        }
    }
}

using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzService
    {
        public async Task StartEinsatzAsync(EinsatzData einsatzData)
        {
            await ApplyStaffelFallbackAsync(einsatzData);
            EnsureAlarmTime(einsatzData);

            _currentEinsatz = einsatzData;
            _teams.Clear();
            _globalNotes.Clear();
            _noteHistory.Clear();
            _dogPauses.Clear();
            EnsureCurrentEinsatzTeamReference();

            var startNote = new GlobalNotesEntry
            {
                Text = $"Einsatz gestartet: {einsatzData.EinsatzTyp} - {einsatzData.Einsatzort}",
                Type = GlobalNotesEntryType.EinsatzUpdate,
                Timestamp = _timeService?.Now ?? Ortszeit.Jetzt
            };
            _globalNotes.Add(startNote);

            EinsatzChanged?.Invoke();
            NoteAdded?.Invoke(startNote);
        }

        public Task UpdateEinsatzAsync(EinsatzData einsatzData)
        {
            _currentEinsatz.IstEinsatz = einsatzData.IstEinsatz;
            _currentEinsatz.Szenario = einsatzData.Szenario;
            _currentEinsatz.EinsatzNummer = einsatzData.EinsatzNummer;
            _currentEinsatz.Einsatzort = einsatzData.Einsatzort;
            _currentEinsatz.MapAddress = einsatzData.MapAddress;
            _currentEinsatz.Alarmiert = einsatzData.Alarmiert;
            _currentEinsatz.AlarmierungsZeit = einsatzData.AlarmierungsZeit;
            _currentEinsatz.AnzahlTeams = einsatzData.AnzahlTeams;
            _currentEinsatz.ExportPfad = einsatzData.ExportPfad;
            _currentEinsatz.Einsatzleiter = einsatzData.Einsatzleiter;
            _currentEinsatz.Fuehrungsassistent = einsatzData.Fuehrungsassistent;

            EinsatzChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task EndEinsatzAsync()
        {
            foreach (var team in _teams)
                team.StopTimer();

            _currentEinsatz.EinsatzEnde = _timeService?.Now ?? Ortszeit.Jetzt;

            var endNote = new GlobalNotesEntry
            {
                Text = "Einsatz beendet",
                Type = GlobalNotesEntryType.EinsatzUpdate,
                Timestamp = _timeService?.Now ?? Ortszeit.Jetzt
            };
            _globalNotes.Add(endNote);
            NoteAdded?.Invoke(endNote);
            EinsatzChanged?.Invoke();

            return Task.CompletedTask;
        }

        public void ResetEinsatz()
        {
            foreach (var team in _teams)
            {
                team.StopTimer();
                team.TimerStarted -= Team_TimerStarted;
                team.TimerStopped -= Team_TimerStopped;
                team.TimerReset -= Team_TimerReset;
                team.WarningTriggered -= Team_WarningTriggered;
            }

            _teams.Clear();
            _globalNotes.Clear();
            _noteHistory.Clear();
            _dogPauses.Clear();

            _currentEinsatz = new EinsatzData
            {
                EinsatzDatum = _timeService?.Now ?? Ortszeit.Jetzt,
                IstEinsatz = true,
                VermisstenInfo = null,
                ElNotizen = new List<ElNotizEntry>()
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
                NoteHistory = _noteHistory.ToList(),
                DogPauses = _dogPauses.Values.ToList()
            };
        }

        public Task ImportRuntimeSnapshotAsync(EinsatzRuntimeSnapshot snapshot)
        {
            if (snapshot == null)
                return Task.CompletedTask;

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
            _dogPauses.Clear();

            _currentEinsatz = snapshot.CurrentEinsatz ?? new EinsatzData();

            foreach (var team in snapshot.Teams ?? new List<Team>())
            {
                team.StartzeitWiederherstellen(_timeService?.Now ?? Ortszeit.Jetzt);
                _teams.Add(team);
                team.TimerStarted += Team_TimerStarted;
                team.TimerStopped += Team_TimerStopped;
                team.TimerReset += Team_TimerReset;
                team.WarningTriggered += Team_WarningTriggered;
            }

            if (snapshot.DogPauses is { Count: > 0 })
            {
                foreach (var record in snapshot.DogPauses)
                    if (!string.IsNullOrWhiteSpace(record.DogId))
                        _dogPauses[record.DogId] = record;
            }
            else
            {
                foreach (var team in _teams.Where(t => t.IsPausing && !string.IsNullOrWhiteSpace(t.DogId) && t.PauseStartTime.HasValue))
                {
                    if (!_dogPauses.TryGetValue(team.DogId, out var existing) || team.RunTimeBeforePause > existing.RunTimeBeforePause)
                    {
                        _dogPauses[team.DogId] = new DogPauseRecord
                        {
                            DogId = team.DogId,
                            DogName = team.DogName,
                            PauseStartTime = team.PauseStartTime!.Value,
                            RunTimeBeforePause = team.RunTimeBeforePause,
                            RequiredPauseMinutes = team.RequiredPauseMinutes
                        };
                    }
                }
            }

            if (snapshot.GlobalNotes != null)
                _globalNotes.AddRange(snapshot.GlobalNotes);

            if (snapshot.NoteHistory != null)
                _noteHistory.AddRange(snapshot.NoteHistory);

            _currentEinsatz.GlobalNotesEntries ??= new List<GlobalNotesEntry>();
            _currentEinsatz.GlobalNotesEntries.Clear();
            _currentEinsatz.GlobalNotesEntries.AddRange(_globalNotes);

            if (_currentEinsatz.SearchAreas != null)
            {
                foreach (var area in _currentEinsatz.SearchAreas)
                {
                    if (!string.IsNullOrWhiteSpace(area.GeoJsonData) &&
                        (area.Coordinates == null || area.Coordinates.Count == 0 ||
                         area.Coordinates.All(c => c.Latitude == 0 && c.Longitude == 0)))
                    {
                        ExtractCoordinatesFromGeoJson(area);
                    }
                }
            }

            EnsureCurrentEinsatzTeamReference();
            MigrateTrackSnapshotsToCompletedSearches();
            EinsatzChanged?.Invoke();

            return Task.CompletedTask;
        }

        private static void ExtractCoordinatesFromGeoJson(SearchArea area)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(area.GeoJsonData);
                if (doc.RootElement.TryGetProperty("geometry", out var geometry) &&
                    geometry.TryGetProperty("coordinates", out var coordinates))
                {
                    area.Coordinates = new List<(double, double)>();
                    var firstRing = coordinates[0];
                    foreach (var coord in firstRing.EnumerateArray())
                        area.Coordinates.Add((coord[1].GetDouble(), coord[0].GetDouble()));
                }
            }
            catch
            {
                // GeoJSON konnte nicht geparst werden – Koordinaten bleiben leer
            }
        }
    }
}

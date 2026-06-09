// Implementierung des GPS-Halsband Tracking-Service
// Thread-safe Singleton, verwaltet bis zu 20 gleichzeitige Halsbänder
// Prüft bei jeder neuen Position ob der Hund im Suchgebiet ist

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    public class CollarTrackingService : ICollarTrackingService, IDisposable
    {
        private readonly IEinsatzService _einsatzService;
        private readonly ConcurrentDictionary<string, Collar> _collars = new();
        private readonly ConcurrentDictionary<string, List<CollarLocation>> _locationHistory = new();
        private readonly ConcurrentDictionary<string, CollarLocation> _latestLocations = new();
        private readonly object _lock = new();

        public event Action<string, CollarLocation>? CollarLocationReceived;
        public event Action<string, string, CollarLocation>? OutOfBoundsDetected;
        public event Action<string>? CollarHistoryCleared;
        public event Action<TeamTrackSnapshot>? TrackSnapshotSaved;

        public IReadOnlyList<Collar> Collars => _collars.Values.ToList().AsReadOnly();

        public CollarTrackingService(IEinsatzService einsatzService)
        {
            _einsatzService = einsatzService;
            _einsatzService.TeamRemoved += OnTeamRemoved;
        }

        public Task<CollarLocation> ReceiveLocationAsync(string collarId, string collarName, double latitude, double longitude, int? batteryLevel = null)
        {
            // Halsband registrieren / aktualisieren
            var collar = _collars.AddOrUpdate(
                collarId,
                new Collar(collarId, collarName),
                (_, existing) =>
                {
                    existing.CollarName = collarName;
                    return existing;
                });

            // Position speichern
            var location = new CollarLocation(collarId, latitude, longitude, DateTime.UtcNow, batteryLevel);
            _latestLocations[collarId] = location;

            // Nur in History speichern wenn das zugewiesene Team aktiv sucht (IsRunning)
            var isRecording = false;
            if (collar.IsAssigned && !string.IsNullOrWhiteSpace(collar.AssignedTeamId))
            {
                var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == collar.AssignedTeamId);
                isRecording = team?.IsRunning == true;
            }

            if (isRecording)
            {
                var history = _locationHistory.GetOrAdd(collarId, _ => new List<CollarLocation>());
                lock (_lock)
                {
                    history.Add(location);
                }
            }

            // Event auslösen für SignalR-Broadcast (immer, auch wenn nicht aufgezeichnet wird)
            CollarLocationReceived?.Invoke(collarId, location);

            // Falls dem Halsband ein Team zugewiesen ist: Bounds-Check
            if (collar.IsAssigned && !string.IsNullOrWhiteSpace(collar.AssignedTeamId))
            {
                CheckBounds(collar.AssignedTeamId, collarId, location);
            }

            return Task.FromResult(location);
        }

        public Task AssignCollarToTeamAsync(string collarId, string teamId)
        {
            var collar = _collars.AddOrUpdate(
                collarId,
                id => new Collar(id, id),
                (_, existing) => existing);

            var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
            if (team == null)
            {
                throw new InvalidOperationException($"Team '{teamId}' nicht gefunden.");
            }

            // Falls das Ziel-Team bereits ein anderes Halsband hatte, dieses freigeben.
            var previousCollarOnTeam = _collars.Values
                .FirstOrDefault(c => c.AssignedTeamId == teamId && !string.Equals(c.Id, collarId, StringComparison.OrdinalIgnoreCase));
            if (previousCollarOnTeam != null)
            {
                previousCollarOnTeam.IsAssigned = false;
                previousCollarOnTeam.AssignedTeamId = null;
            }

            // Alte Zuordnung entfernen (falls vorhanden)
            var previousTeam = _einsatzService.Teams.FirstOrDefault(t => t.CollarId == collarId);
            if (previousTeam != null)
            {
                previousTeam.CollarId = null;
                previousTeam.CollarName = null;
            }

            // Teamname aus bestehender Team-Zuordnung beibehalten, falls das Halsband neu angelegt wurde.
            if (!string.IsNullOrWhiteSpace(team.CollarName)
                && (string.IsNullOrWhiteSpace(collar.CollarName)
                    || string.Equals(collar.CollarName, collar.Id, StringComparison.OrdinalIgnoreCase)))
            {
                collar.CollarName = team.CollarName;
            }

            // Neue Zuordnung setzen
            collar.IsAssigned = true;
            collar.AssignedTeamId = teamId;
            team.CollarId = collarId;
            team.CollarName = collar.CollarName;

            return Task.CompletedTask;
        }

        public Task UnassignCollarAsync(string collarId)
        {
            if (_collars.TryGetValue(collarId, out var collar))
            {
                if (!string.IsNullOrWhiteSpace(collar.AssignedTeamId))
                {
                    var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == collar.AssignedTeamId);
                    if (team != null)
                    {
                        team.CollarId = null;
                        team.CollarName = null;
                    }
                }

                collar.IsAssigned = false;
                collar.AssignedTeamId = null;
            }

            return Task.CompletedTask;
        }

        public IReadOnlyList<Collar> GetAvailableCollars()
        {
            // Auch Halsbänder mit verwaister Zuweisung (Team existiert nicht mehr) als verfügbar anbieten
            return _collars.Values
                .Where(c => !c.IsAssigned ||
                            (c.AssignedTeamId != null && !_einsatzService.Teams.Any(t => t.TeamId == c.AssignedTeamId)))
                .ToList().AsReadOnly();
        }

        public IReadOnlyList<CollarLocation> GetLocationHistory(string collarId)
        {
            if (_locationHistory.TryGetValue(collarId, out var history))
            {
                lock (_lock)
                {
                    return history.ToList().AsReadOnly();
                }
            }

            return Array.Empty<CollarLocation>();
        }

        public CollarLocation? GetLatestLocation(string collarId)
        {
            return _latestLocations.TryGetValue(collarId, out var latest) ? latest : null;
        }

        public void ClearCollarHistory(string collarId)
        {
            if (_locationHistory.TryGetValue(collarId, out var history))
            {
                lock (_lock)
                {
                    history.Clear();
                }
            }
            _latestLocations.TryRemove(collarId, out _);
            CollarHistoryCleared?.Invoke(collarId);
        }

        public void SetLocationHistory(string collarId, List<CollarLocation> history)
        {
            if (history == null || history.Count == 0) return;
            var list = _locationHistory.GetOrAdd(collarId, _ => new List<CollarLocation>());
            lock (_lock)
            {
                list.Clear();
                list.AddRange(history);
            }
            var last = history[history.Count - 1];
            _latestLocations[collarId] = last;
        }

        public void NotifySnapshotSaved(TeamTrackSnapshot snapshot)
        {
            TrackSnapshotSaved?.Invoke(snapshot);
        }

        public void ClearAll()
        {
            _collars.Clear();
            _locationHistory.Clear();
            _latestLocations.Clear();
        }

        private void OnTeamRemoved(Team removedTeam)
        {
            if (string.IsNullOrWhiteSpace(removedTeam.CollarId))
                return;

            if (_collars.TryGetValue(removedTeam.CollarId, out var collar)
                && string.Equals(collar.AssignedTeamId, removedTeam.TeamId, StringComparison.OrdinalIgnoreCase))
            {
                collar.IsAssigned = false;
                collar.AssignedTeamId = null;
            }
        }

        public void Dispose()
        {
            _einsatzService.TeamRemoved -= OnTeamRemoved;
        }

        private void CheckBounds(string teamId, string collarId, CollarLocation location)
        {
            var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
            // OOB-Warnungen erst waehrend aktiver Suche, nicht auf dem Anmarsch ins Suchgebiet.
            if (team == null || !team.IsRunning || string.IsNullOrWhiteSpace(team.SearchAreaId))
                return;

            var searchArea = _einsatzService.CurrentEinsatz.SearchAreas
                .FirstOrDefault(a => a.Id == team.SearchAreaId);

            if (searchArea == null || searchArea.Coordinates == null || searchArea.Coordinates.Count < 3)
                return;

            if (!IsPointInPolygon(location.Latitude, location.Longitude, searchArea.Coordinates))
            {
                OutOfBoundsDetected?.Invoke(teamId, collarId, location);
            }
        }

        /// <summary>
        /// Ray-Casting Algorithmus: Prüft ob ein Punkt innerhalb eines Polygons liegt
        /// </summary>
        private static bool IsPointInPolygon(double lat, double lng, List<(double Latitude, double Longitude)> polygon)
        {
            bool inside = false;
            int n = polygon.Count;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                if ((pi.Longitude > lng) != (pj.Longitude > lng) &&
                    lat < (pj.Latitude - pi.Latitude) * (lng - pi.Longitude) / (pj.Longitude - pi.Longitude) + pi.Latitude)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}

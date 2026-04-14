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
    public class CollarTrackingService : ICollarTrackingService
    {
        private readonly IEinsatzService _einsatzService;
        private readonly ConcurrentDictionary<string, Collar> _collars = new();
        private readonly ConcurrentDictionary<string, List<CollarLocation>> _locationHistory = new();
        private readonly object _lock = new();

        public event Action<string, CollarLocation>? CollarLocationReceived;
        public event Action<string, string, CollarLocation>? OutOfBoundsDetected;
        public event Action<string>? CollarHistoryCleared;

        public IReadOnlyList<Collar> Collars => _collars.Values.ToList().AsReadOnly();

        public CollarTrackingService(IEinsatzService einsatzService)
        {
            _einsatzService = einsatzService;
        }

        public Task<CollarLocation> ReceiveLocationAsync(string collarId, string collarName, double latitude, double longitude)
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
            var location = new CollarLocation(collarId, latitude, longitude, DateTime.UtcNow);

            // Nur in History speichern wenn das zugewiesene Team aktiv sucht (IsRunning)
            var isRecording = false;
            if (collar.IsAssigned && !string.IsNullOrEmpty(collar.AssignedTeamId))
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
            if (collar.IsAssigned && !string.IsNullOrEmpty(collar.AssignedTeamId))
            {
                CheckBounds(collar.AssignedTeamId, collarId, location);
            }

            return Task.FromResult(location);
        }

        public Task AssignCollarToTeamAsync(string collarId, string teamId)
        {
            if (!_collars.TryGetValue(collarId, out var collar))
            {
                throw new InvalidOperationException($"Halsband '{collarId}' nicht gefunden.");
            }

            var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
            if (team == null)
            {
                throw new InvalidOperationException($"Team '{teamId}' nicht gefunden.");
            }

            // Alte Zuordnung entfernen (falls vorhanden)
            var previousTeam = _einsatzService.Teams.FirstOrDefault(t => t.CollarId == collarId);
            if (previousTeam != null)
            {
                previousTeam.CollarId = null;
                previousTeam.CollarName = null;
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
                if (!string.IsNullOrEmpty(collar.AssignedTeamId))
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
            return _collars.Values.Where(c => !c.IsAssigned).ToList().AsReadOnly();
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

        public void ClearCollarHistory(string collarId)
        {
            if (_locationHistory.TryGetValue(collarId, out var history))
            {
                lock (_lock)
                {
                    history.Clear();
                }
            }
            CollarHistoryCleared?.Invoke(collarId);
        }

        public void ClearAll()
        {
            _collars.Clear();
            _locationHistory.Clear();
        }

        private void CheckBounds(string teamId, string collarId, CollarLocation location)
        {
            var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
            if (team == null || string.IsNullOrEmpty(team.SearchAreaId))
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

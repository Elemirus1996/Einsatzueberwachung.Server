using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzService
    {
        public event Action<TeamTrackSnapshot>? TrackSnapshotAdded;
        public event Action<CompletedSearch>? CompletedSearchUpdated;

        /// <summary>
        /// Legt eine neue abgeschlossene Suchepisode für ein Team an.
        /// </summary>
        public Task<CompletedSearch> CreateCompletedSearchAsync(string teamId, DateTime start, DateTime end, string? searchAreaId = null)
        {
            var team = _teams.FirstOrDefault(t => t.TeamId == teamId)
                       ?? throw new ArgumentException($"Team '{teamId}' nicht gefunden.", nameof(teamId));

            var area = searchAreaId is not null
                ? _currentEinsatz.SearchAreas?.FirstOrDefault(a => a.Id == searchAreaId)
                : null;

            var search = new CompletedSearch
            {
                TeamId = team.TeamId,
                TeamName = team.TeamName,
                SearchStart = start,
                SearchEnd = end,
                SearchAreaId = area?.Id,
                SearchAreaName = area?.Name,
                Einsatzart = team.SucheEinsatzart ?? team.Einsatzart
            };

            _currentEinsatz.CompletedSearches ??= new List<CompletedSearch>();
            _currentEinsatz.CompletedSearches.Add(search);

            CompletedSearchUpdated?.Invoke(search);
            return Task.FromResult(search);
        }

        /// <summary>
        /// Fügt einen Track-Snapshot zu einer bestehenden abgeschlossenen Suche hinzu.
        /// Wirft <see cref="InvalidOperationException"/> wenn der TrackType bereits vorhanden ist.
        /// </summary>
        public Task AddTrackToCompletedSearchAsync(string completedSearchId, TeamTrackSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            _currentEinsatz.CompletedSearches ??= new List<CompletedSearch>();
            var search = _currentEinsatz.CompletedSearches.FirstOrDefault(cs => cs.Id == completedSearchId)
                         ?? throw new ArgumentException($"CompletedSearch '{completedSearchId}' nicht gefunden.", nameof(completedSearchId));

            var canAdd = snapshot.TrackType == TrackType.CollarTrack ? search.CanAddCollarTrack : search.CanAddHumanTrack;
            if (!canAdd)
                throw new InvalidOperationException($"Diese Suche enthält bereits einen Track vom Typ '{snapshot.TrackType}'.");

            NormalizeSnapshotForCompletedSearch(search, snapshot);

            search.Tracks.Add(snapshot);

            // Flat-List für Rückwärtskompatibilität mitpflegen
            _currentEinsatz.TrackSnapshots ??= new List<TeamTrackSnapshot>();
            _currentEinsatz.TrackSnapshots.Add(snapshot);

            TrackSnapshotAdded?.Invoke(snapshot);
            CompletedSearchUpdated?.Invoke(search);
            return Task.CompletedTask;
        }

        private void NormalizeSnapshotForCompletedSearch(CompletedSearch search, TeamTrackSnapshot snapshot)
        {
            snapshot.TeamId = search.TeamId;
            snapshot.TeamName = search.TeamName;
            snapshot.SearchAreaName = search.SearchAreaName ?? string.Empty;
            snapshot.SearchAreaCoordinates = new List<(double Latitude, double Longitude)>();
            snapshot.SearchAreaColor = string.Empty;

            if (string.IsNullOrWhiteSpace(search.SearchAreaId))
                return;

            var searchArea = _currentEinsatz.SearchAreas?.FirstOrDefault(area => area.Id == search.SearchAreaId);
            if (searchArea == null)
                return;

            snapshot.SearchAreaName = !string.IsNullOrWhiteSpace(search.SearchAreaName)
                ? search.SearchAreaName!
                : searchArea.Name;

            if (searchArea.Coordinates is { Count: >= 3 })
                snapshot.SearchAreaCoordinates = new List<(double Latitude, double Longitude)>(searchArea.Coordinates);

            snapshot.SearchAreaColor = searchArea.Color ?? string.Empty;
        }

        /// <summary>
        /// Legacy-Wrapper: Fügt einen Track-Snapshot direkt hinzu und erzeugt implizit eine neue
        /// <see cref="CompletedSearch"/> mit Zeiten aus den Track-Punkten.
        /// Bestehender Code (z.B. Live-Recording über CollarTrackingService) bleibt unberührt.
        /// </summary>
        public Task AddTrackSnapshotAsync(TeamTrackSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var start = snapshot.Points.Count > 0 ? snapshot.Points[0].Timestamp : snapshot.CapturedAt;
            var end = snapshot.Points.Count > 0 ? snapshot.Points[^1].Timestamp : snapshot.CapturedAt;

            var search = new CompletedSearch
            {
                TeamId = snapshot.TeamId,
                TeamName = snapshot.TeamName,
                SearchStart = start,
                SearchEnd = end,
                SearchAreaName = snapshot.SearchAreaName
            };
            search.Tracks.Add(snapshot);

            _currentEinsatz.CompletedSearches ??= new List<CompletedSearch>();
            _currentEinsatz.CompletedSearches.Add(search);

            _currentEinsatz.TrackSnapshots ??= new List<TeamTrackSnapshot>();
            _currentEinsatz.TrackSnapshots.Add(snapshot);

            TrackSnapshotAdded?.Invoke(snapshot);
            CompletedSearchUpdated?.Invoke(search);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Migriert veraltete TrackSnapshots (flache Liste) in CompletedSearches.
        /// Wird beim Laden eines alten Snapshots einmalig ausgeführt.
        /// </summary>
        private void MigrateTrackSnapshotsToCompletedSearches()
        {
            _currentEinsatz.CompletedSearches ??= new List<CompletedSearch>();

            if (_currentEinsatz.CompletedSearches.Count > 0)
                return; // Bereits migriert

            if (_currentEinsatz.TrackSnapshots == null || _currentEinsatz.TrackSnapshots.Count == 0)
                return;

            foreach (var snapshot in _currentEinsatz.TrackSnapshots)
            {
                var start = snapshot.Points.Count > 0 ? snapshot.Points[0].Timestamp : snapshot.CapturedAt;
                var end = snapshot.Points.Count > 0 ? snapshot.Points[^1].Timestamp : snapshot.CapturedAt;

                var search = new CompletedSearch
                {
                    TeamId = snapshot.TeamId,
                    TeamName = snapshot.TeamName,
                    SearchStart = start,
                    SearchEnd = end,
                    SearchAreaName = snapshot.SearchAreaName
                };
                search.Tracks.Add(snapshot);
                _currentEinsatz.CompletedSearches.Add(search);
            }
        }
    }
}

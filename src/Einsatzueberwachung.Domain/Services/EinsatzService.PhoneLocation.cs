using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzService
    {
        private readonly object _phoneLocationsLock = new();

        // Aufgezeichneter GPS-Pfad pro Team (nur während IsRunning == true)
        private readonly Dictionary<string, List<TeamPhoneLocation>> _phoneTrackHistory = new();

        public Task UpdateTeamPhoneLocationAsync(string teamId, double lat, double lng, double? accuracy = null, DateTime? timestampUtc = null)
        {
            if (string.IsNullOrWhiteSpace(teamId))
                return Task.CompletedTask;

            if (double.IsNaN(lat) || double.IsInfinity(lat) || lat < -90 || lat > 90)
                return Task.CompletedTask;
            if (double.IsNaN(lng) || double.IsInfinity(lng) || lng < -180 || lng > 180)
                return Task.CompletedTask;
            if (accuracy.HasValue && (double.IsNaN(accuracy.Value) || double.IsInfinity(accuracy.Value)))
                accuracy = null;

            Team? team;
            lock (_teams)
            {
                team = _teams.FirstOrDefault(t => t.TeamId == teamId);
            }
            if (team == null) return Task.CompletedTask;

            // Zeitpunkt aus dem Puffer der Traccar-App (UTC) in die lokale Serverzeit umrechnen; ohne Angabe: jetzt
            var now = GetServerNowLocal();
            var timestamp = now;
            if (timestampUtc.HasValue)
            {
                timestamp = timestampUtc.Value + (now - DateTime.UtcNow);
                if (timestamp > now) timestamp = now;
                if (timestamp < now.AddHours(-24)) return Task.CompletedTask;
            }

            var loc = new TeamPhoneLocation
            {
                Latitude = lat,
                Longitude = lng,
                Timestamp = timestamp,
                Accuracy = accuracy
            };

            bool isNewest;
            lock (_phoneLocationsLock)
            {
                // Nachgesendete aeltere Punkte ersetzen nicht die aktuelle Position
                isNewest = !_phoneLocations.TryGetValue(teamId, out var latest) || loc.Timestamp >= latest.Timestamp;
                if (isNewest) _phoneLocations[teamId] = loc;

                // Punkt nur aufzeichnen wenn das Team aktiv sucht – zeitlich einsortiert
                if (team.IsRunning)
                {
                    if (!_phoneTrackHistory.TryGetValue(teamId, out var trackList))
                    {
                        trackList = new List<TeamPhoneLocation>();
                        _phoneTrackHistory[teamId] = trackList;
                    }
                    var index = trackList.Count;
                    while (index > 0 && trackList[index - 1].Timestamp > loc.Timestamp) index--;
                    trackList.Insert(index, loc);
                }
            }

            if (!isNewest) return Task.CompletedTask;

            try
            {
                TeamPhoneLocationChanged?.Invoke(teamId, team.TeamName, loc);
                if (team.IsRunning)
                    TeamPhoneTrackPointAdded?.Invoke(teamId, team.TeamName, loc);
            }
            catch
            {
                // Subscriber-Fehler dürfen den Request-Pfad nicht abbrechen
            }
            return Task.CompletedTask;
        }

        public IReadOnlyList<TeamPhoneLocation> GetPhoneTrackHistory(string teamId)
        {
            lock (_phoneLocationsLock)
            {
                if (_phoneTrackHistory.TryGetValue(teamId, out var list))
                    return list.ToList().AsReadOnly();
                return Array.Empty<TeamPhoneLocation>();
            }
        }

        public void ClearPhoneTrackHistory(string teamId)
        {
            lock (_phoneLocationsLock)
            {
                _phoneTrackHistory.Remove(teamId);
            }
        }

        public void SetPhoneTrackHistory(string teamId, List<TeamPhoneLocation> history)
        {
            if (history == null || history.Count == 0) return;
            lock (_phoneLocationsLock)
            {
                _phoneTrackHistory[teamId] = new List<TeamPhoneLocation>(history);
            }
        }

        public IReadOnlyDictionary<string, IReadOnlyList<TeamPhoneLocation>> GetAllPhoneTrackHistories()
        {
            lock (_phoneLocationsLock)
            {
                return _phoneTrackHistory
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => (IReadOnlyList<TeamPhoneLocation>)kvp.Value.ToList().AsReadOnly());
            }
        }
    }
}

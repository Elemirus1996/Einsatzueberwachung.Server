using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzService
    {
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
                return Task.CompletedTask;

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
                return Task.CompletedTask;

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

        public Task SetElwPositionAsync(double latitude, double longitude)
        {
            _currentEinsatz.ElwPosition = (latitude, longitude);
            EinsatzChanged?.Invoke();
            return Task.CompletedTask;
        }

        public Task ClearElwPositionAsync()
        {
            _currentEinsatz.ElwPosition = null;
            EinsatzChanged?.Invoke();
            return Task.CompletedTask;
        }

        public event Action<MapMarker, string>? MapMarkerChanged;

        public Task<MapMarker> AddMapMarkerAsync(MapMarker marker)
        {
            if (string.IsNullOrWhiteSpace(marker.UtmZone))
            {
                var (zone, band, easting, northing) = UtmConverter.LatLongToUtm(marker.Latitude, marker.Longitude);
                marker.UtmZone = UtmConverter.FormatUtmZone(zone, band);
                marker.UtmEasting = easting;
                marker.UtmNorthing = northing;
            }
            _currentEinsatz.MapMarkers.Add(marker);
            EinsatzChanged?.Invoke();
            MapMarkerChanged?.Invoke(marker, KartenpunktAenderung.Neu);
            return Task.FromResult(marker);
        }

        public Task<MapMarker?> UpdateMapMarkerAsync(string markerId, string? label = null, double? latitude = null, double? longitude = null)
        {
            var marker = _currentEinsatz.MapMarkers.FirstOrDefault(m => m.Id == markerId);
            if (marker == null)
                return Task.FromResult<MapMarker?>(null);

            if (label != null)
                marker.Label = label;

            if (latitude.HasValue && longitude.HasValue)
            {
                marker.Latitude = latitude.Value;
                marker.Longitude = longitude.Value;
                var (zone, band, easting, northing) = UtmConverter.LatLongToUtm(latitude.Value, longitude.Value);
                marker.UtmZone = UtmConverter.FormatUtmZone(zone, band);
                marker.UtmEasting = easting;
                marker.UtmNorthing = northing;
            }

            EinsatzChanged?.Invoke();
            MapMarkerChanged?.Invoke(marker, KartenpunktAenderung.Geaendert);
            return Task.FromResult<MapMarker?>(marker);
        }

        public Task RemoveMapMarkerAsync(string markerId)
        {
            var marker = _currentEinsatz.MapMarkers.FirstOrDefault(m => m.Id == markerId);
            if (marker != null)
            {
                _currentEinsatz.MapMarkers.Remove(marker);
                EinsatzChanged?.Invoke();
                MapMarkerChanged?.Invoke(marker, KartenpunktAenderung.Geloescht);
            }
            return Task.CompletedTask;
        }

        public Task<List<MapMarker>> GetMapMarkersAsync()
            => Task.FromResult(_currentEinsatz.MapMarkers.ToList());
    }
}

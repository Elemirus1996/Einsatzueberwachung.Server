using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Components;
using Einsatzueberwachung.Server.Components.Monitor;
using Einsatzueberwachung.Server.Components.Shared;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{    private async Task LoadMasterDataAsync()
    {
        _personalList = await MasterDataService.GetPersonalListAsync();
        _dogList = await MasterDataService.GetDogListAsync();
        _droneList = await MasterDataService.GetDroneListAsync();
    }

    private IReadOnlyList<MentionSuggestion> GetNoteMentions()
    {
        var result = new List<MentionSuggestion>();

        // ── Teams ────────────────────────────────────────────────────────────
        foreach (var team in EinsatzService.Teams.OrderBy(t => t.TeamName))
        {
            string subtitle;
            if (team.IsDroneTeam)
                subtitle = string.IsNullOrWhiteSpace(team.DroneType) ? "Drohnenteam" : $"Drohne: {team.DroneType}";
            else if (team.IsSupportTeam)
                subtitle = "Unterstützungsteam";
            else if (!string.IsNullOrWhiteSpace(team.DogName))
                subtitle = string.IsNullOrWhiteSpace(team.HundefuehrerName)
                    ? $"Hund: {team.DogName}"
                    : $"Hund: {team.DogName} · Führer: {team.HundefuehrerName}";
            else
                subtitle = string.IsNullOrWhiteSpace(team.HundefuehrerName) ? string.Empty : $"Führer: {team.HundefuehrerName}";

            result.Add(new MentionSuggestion(team.TeamId, team.TeamName, MentionType.Team, subtitle));
        }

        // ── Persons ──────────────────────────────────────────────────────────
        var activePersons = SelectionListOrdering
            .OrderPersonnelByFirstName(_personalList.Where(person => person.IsActive))
            .ToList();
        var personNameGroups = activePersons.GroupBy(p => p.FullName).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var person in activePersons)
        {
            // Disambiguate duplicate full names: ALL copies get a numeric suffix (1), (2), …
            var displayName = person.FullName;
            var group = personNameGroups[person.FullName];
            if (group.Count > 1)
            {
                var idx = group.IndexOf(person) + 1;
                displayName = $"{person.FullName} ({idx})";
            }

            var subtitle = person.Skills != Domain.Models.Enums.PersonalSkills.None
                ? person.SkillsShortDisplay
                : string.Empty;

            result.Add(new MentionSuggestion(person.Id, displayName, MentionType.Person, subtitle));
        }

        // ── Dogs ─────────────────────────────────────────────────────────────
        var activeDogs = _dogList.Where(d => d.IsActive).OrderBy(d => d.Name).ToList();
        var dogNameGroups = activeDogs.GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var dog in activeDogs)
        {
            // Resolve primary handler name for subtitle and disambiguation
            var handlerName = dog.HundefuehrerIds.Count > 0
                ? _personalList.FirstOrDefault(p => p.Id == dog.HundefuehrerIds[0])?.FullName ?? string.Empty
                : string.Empty;

            // When two or more dogs share the same name, disambiguate:
            // prefer handler name, but add a numeric suffix when that still would not be unique.
            var displayName = dog.Name;
            var group = dogNameGroups[dog.Name];
            if (group.Count > 1)
            {
                if (!string.IsNullOrWhiteSpace(handlerName))
                {
                    var sameHandlerGroup = group
                        .Where(d =>
                        {
                            var primaryHandlerName = d.HundefuehrerIds.Count > 0
                                ? _personalList.FirstOrDefault(p => p.Id == d.HundefuehrerIds[0])?.FullName ?? string.Empty
                                : string.Empty;
                            return string.Equals(primaryHandlerName, handlerName, StringComparison.Ordinal);
                        })
                        .ToList();

                    displayName = sameHandlerGroup.Count > 1
                        ? $"{dog.Name} / {handlerName} ({sameHandlerGroup.IndexOf(dog) + 1})"
                        : $"{dog.Name} / {handlerName}";
                }
                else
                {
                    var dogsWithoutHandler = group
                        .Where(d => d.HundefuehrerIds.Count == 0)
                        .ToList();

                    displayName = $"{dog.Name} ({dogsWithoutHandler.IndexOf(dog) + 1})";
                }
            }

            // Subtitle: breed + handler (handler only when not already embedded in displayName)
            var subtitleParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(dog.Rasse)) subtitleParts.Add(dog.Rasse);
            if (!string.IsNullOrWhiteSpace(handlerName) && displayName == dog.Name)
                subtitleParts.Add($"Führer: {handlerName}");

            result.Add(new MentionSuggestion(dog.Id, displayName, MentionType.Hund, string.Join(", ", subtitleParts)));
        }

        return result;
    }

    private void LoadQuickNoteTemplates()
    {
        _quickNoteTemplates = _appSettings.QuickNoteTemplates?
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string>();

        if (_quickNoteTemplates.Count == 0)
        {
            _quickNoteTemplates = new List<string>
            {
                "ELW Ankunft Einsatzstelle",
                "ELW verlässt Einsatzstelle",
                "Team vor Ort eingetroffen",
                "Lagemeldung an Leitstelle",
                "Suche gestartet",
                "Suche beendet"
            };
        }
    }

    private void OnEinsatzChanged()
    {
        _ = InvokeAsync(async () =>
        {
            UpdateMissionTopbar();
            await RefreshMonitorWeatherAsync(forceGeocoding: true);
            StateHasChanged();
        });
    }

    private void OnTeamChanged(Team changed)
    {
        // Remove expand state for teams that were deleted
        var activeIds = EinsatzService.Teams.Select(t => t.TeamId).ToHashSet();
        _expandedTeamIds.IntersectWith(activeIds);

        InvokeAsync(StateHasChanged);
    }

    private void OnTeamsReordered()
    {
        InvokeAsync(StateHasChanged);
    }

    private void OnNoteAdded(GlobalNotesEntry _)
    {
        InvokeAsync(StateHasChanged);
    }

    private void OnCollarLocationChanged(string _, CollarLocation __)
    {
        InvokeAsync(StateHasChanged);
    }

    private void OnCollarHistoryChanged(string _)
    {
        InvokeAsync(StateHasChanged);
    }

    private void StartWeatherRefreshTimer()
    {
        _weatherRefreshTimer?.Dispose();
        _weatherRefreshTimer = new System.Threading.Timer(
            _ => _ = InvokeAsync(() => RefreshMonitorWeatherAsync(forceGeocoding: false)),
            null,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10));
    }

    private void StartDurationRefreshTimer()
    {
        _durationRefreshTimer?.Dispose();
        _durationRefreshTimer = new System.Threading.Timer(
            _ => _ = InvokeAsync(async () =>
            {
                await RefreshEinsatzdauerFromHeaderTimeAsync();
                StateHasChanged();
            }),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private async Task RefreshEinsatzdauerFromHeaderTimeAsync()
    {
        if (!EinsatzService.CurrentEinsatz.AlarmierungsZeit.HasValue)
        {
            _einsatzdauerFromHeader = null;
            return;
        }

        try
        {
            var dto = await JS.InvokeAsync<ClientLocalNowDto>("layoutTools.getClientLocalNow");
            var headerNow = new DateTime(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, dto.Second, DateTimeKind.Unspecified);
            var duration = headerNow - EinsatzService.CurrentEinsatz.AlarmierungsZeit.Value;
            _einsatzdauerFromHeader = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        }
        catch
        {
            _einsatzdauerFromHeader = EinsatzService.CurrentEinsatz.Dauer;
        }
    }

    private string GetEinsatzdauerDisplay()
    {
        return (_einsatzdauerFromHeader ?? EinsatzService.CurrentEinsatz.Dauer)?.ToString(@"hh\:mm\:ss") ?? "--:--:--";
    }

    private async Task RefreshMonitorWeatherAsync(bool forceGeocoding)
    {
        _monitorWeatherError = string.Empty;
        _monitorWeatherLoading = true;

        try
        {
            var coordinates = await ResolveEinsatzCoordinatesAsync(forceGeocoding);
            if (coordinates is null)
            {
                if (_monitorWeather is not null)
                {
                    _monitorWeatherUsingFallback = true;
                }

                return;
            }

            var weather = await WeatherService.GetCurrentWeatherAsync(coordinates.Value.Latitude, coordinates.Value.Longitude);
            var forecast = await WeatherService.GetForecastAsync(coordinates.Value.Latitude, coordinates.Value.Longitude);

            if (weather is null)
            {
                if (_monitorWeather is null)
                {
                    _monitorWeatherError = "Wetterdaten konnten aktuell nicht geladen werden.";
                }
                else
                {
                    _monitorWeatherUsingFallback = true;
                    _monitorWeatherError = "Live-Aktualisierung fehlgeschlagen. Letzte gültige Wetterdaten werden angezeigt.";
                }

                return;
            }

            _monitorWeather = weather;
            _monitorForecast = forecast;
            _monitorWeatherLoadedAtLocal = Ortszeit.Jetzt;
            _monitorWeatherUsingFallback = false;
        }
        catch
        {
            if (_monitorWeather is null)
            {
                _monitorWeatherError = "Wetterdienst derzeit nicht erreichbar.";
            }
            else
            {
                _monitorWeatherUsingFallback = true;
                _monitorWeatherError = "Wetterdienst derzeit nicht erreichbar. Letzte gültige Daten bleiben sichtbar.";
            }
        }
        finally
        {
            _monitorWeatherLoading = false;
        }
    }

    private async Task<(double Latitude, double Longitude)?> ResolveEinsatzCoordinatesAsync(bool forceGeocoding)
    {
        var address = EinsatzService.CurrentEinsatz.Einsatzort;
        if (string.IsNullOrWhiteSpace(address))
        {
            _monitorWeatherError = "Kein Einsatzort gesetzt. Wetterdaten können nicht geladen werden.";
            return null;
        }

        if (_einsatzCoordinates.HasValue && !forceGeocoding)
        {
            return _einsatzCoordinates;
        }

        var coordinates = await WeatherService.GeocodeAddressAsync(address);
        if (coordinates is null)
        {
            _monitorWeatherError = "Einsatzort konnte nicht geocodiert werden.";
            return null;
        }

        _einsatzCoordinates = coordinates;
        return coordinates;
    }

    private IEnumerable<WeatherData> GetMonitorForecastItems()
    {
        if (_monitorForecast?.StundenVorhersage is null || _monitorForecast.StundenVorhersage.Length == 0)
        {
            return Enumerable.Empty<WeatherData>();
        }

        var nowUtc = DateTime.UtcNow.AddMinutes(-30);
        return _monitorForecast.StundenVorhersage
            .Where(item => item.Zeitpunkt >= nowUtc)
            .OrderBy(item => item.Zeitpunkt)
            .Take(6);
    }

    private MonitorWeatherMetricTrend GetWeatherTrend(
        string metricName,
        Func<WeatherData, double> getValue,
        int precision,
        bool heatSensitive = false)
    {
        if (_monitorWeather is null)
        {
            return MonitorWeatherMetricTrend.Unavailable;
        }

        var forecast = GetMonitorForecastItems().Take(3).ToArray();
        if (forecast.Length == 0)
        {
            return MonitorWeatherMetricTrend.Unavailable;
        }

        var currentValue = Math.Round(getValue(_monitorWeather), precision, MidpointRounding.AwayFromZero);
        var forecastPeak = Math.Round(forecast.Max(getValue), precision, MidpointRounding.AwayFromZero);
        var isHeatRelevant = !heatSensitive
            || _monitorWeather.Temperatur > 30
            || forecast.Any(item => item.Temperatur > 30);

        if (forecastPeak == currentValue)
        {
            return new(true, "bi-arrow-right-short", $"{metricName}: bleibt in den nächsten Stunden etwa gleich.", "monitor-weather-trend--stable");
        }

        var isIncreasing = forecastPeak > currentValue;
        var isWorsening = isIncreasing && isHeatRelevant;
        var iconClass = isIncreasing ? "bi-arrow-up-short" : "bi-arrow-down-short";
        var stateClass = isWorsening
            ? "monitor-weather-trend--worsening"
            : isHeatRelevant
                ? "monitor-weather-trend--improving"
                : "monitor-weather-trend--stable";
        var change = isIncreasing ? "nimmt zu" : "nimmt ab";

        return new(true, iconClass, $"{metricName} {change}.", stateClass);
    }

    private enum TeamStatusFilter
    {
        All,
        Running,
        Pausing,
        Ready,
        Critical
    }


}

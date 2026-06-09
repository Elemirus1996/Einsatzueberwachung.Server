using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;
using Einsatzueberwachung.Server.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{
    [Inject] IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] IMasterDataService MasterDataService { get; set; } = default!;
    [Inject] IWeatherService WeatherService { get; set; } = default!;
    [Inject] IArchivService ArchivService { get; set; } = default!;
    [Inject] IPdfExportService PdfExportService { get; set; } = default!;
    [Inject] ISettingsService SettingsService { get; set; } = default!;
    [Inject] ITimeService TimeService { get; set; } = default!;
    [Inject] ICollarTrackingService CollarTrackingService { get; set; } = default!;
    [Inject] IDashboardLayoutService DashboardLayoutService { get; set; } = default!;
    [Inject] IJSRuntime JS { get; set; } = default!;
    [Inject] NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "scrollTo")]
    public string? ScrollToTeamId { get; set; }

    [SupplyParameterFromQuery(Name = "openCloseMissionAt")]
    public string? OpenCloseMissionAt { get; set; }

    private readonly TeamEditorModel _teamForm = new();
    private readonly Dictionary<string, string> _replyTexts = new();
    private readonly Dictionary<string, string> _replySourceIds = new();
    private readonly Dictionary<string, bool> _historyVisible = new();
    private readonly Dictionary<string, List<GlobalNotesHistory>> _historyCache = new();

    private string _newNoteText = string.Empty;
    private string _newNoteType = "Notiz";
    private string _newNoteSourceId = string.Empty;
    private string? _editingTeamId;
    private string _teamFormMessage = string.Empty;
    private bool _teamFormIsError;
    private string _teamStatusMessage = string.Empty;
    private bool _teamStatusIsError;
    private bool _showTeamModal;
    private bool _showCloseMissionModal;
    private bool _showPauseResetModal;
    private bool _showEditEinsatzModal;
    private bool _showVermisstenModal;
    private string _vermisstenMessage = string.Empty;
    private bool _vermisstenIsError;
    private Einsatzueberwachung.Domain.Models.VermisstenInfo _vForm = new();

    private bool _einsatzInfoDetailsExpanded;
    private bool _wetterDetailsExpanded;
    private bool _vermisstenDetailsExpanded;
    private Guid? _vermisstenPanelSelectedId;
    private bool _isNoteFormExpanded;

    // Halsband-Auswahl beim Start (falls noch kein Halsband zugewiesen)
    private bool _showCollarSelectModal;
    private string? _collarSelectTeamId;
    private string _collarSelectCollarId = string.Empty;
    private string _editEinsatzMessage = string.Empty;
    private bool _editEinsatzIsError;
    private EditEinsatzForm _editEinsatzForm = new();
    private string? _pauseResetTeamId;
    private bool _closeMissionChecklistConfirmed;
    private bool _closingMission;
    private bool _closeMissionIncludeTracks;
    private string _closeMissionResult = string.Empty;
    private TimeOnly? _closeMissionEndTime;
    private string _closeMissionRemarks = string.Empty;
    private string _closeMissionModalError = string.Empty;
    private string _closeMissionStatus = string.Empty;
    private bool _closeMissionStatusIsError;
    private int _closeMissionTrackCount => EinsatzService.CurrentEinsatz.TrackSnapshots?.Count ?? 0;

    private List<PersonalEntry> _personalList = new();
    private List<DogEntry> _dogList = new();
    private List<DroneEntry> _droneList = new();
    private readonly HashSet<string> _selectedPersonnelIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedDogIds = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] _closeMissionResultOptions =
    {
        "Person gefunden - lebend",
        "Person gefunden - verstorben",
        "Person anderweitig gefunden",
        "Abbruch - Wetter",
        "Abbruch - Dunkelheit",
        "Suche erfolglos abgeschlossen",
        "Uebung abgeschlossen"
    };

    private List<string> _quickNoteTemplates = new();

    private WeatherData? _monitorWeather;
    private WeatherForecast? _monitorForecast;
    private DateTime _monitorWeatherLoadedAtLocal;
    private string _monitorWeatherError = string.Empty;
    private bool _monitorWeatherLoading;
    private bool _monitorWeatherUsingFallback;
    private (double Latitude, double Longitude)? _einsatzCoordinates;
    private System.Threading.Timer? _weatherRefreshTimer;
    private System.Threading.Timer? _durationRefreshTimer;
    private TimeSpan? _einsatzdauerFromHeader;
    private AppSettings _appSettings = new();

    // Dashboard-Layout
    private List<DashboardPanelConfig> _currentLayout = new();
    private bool _showPanelPicker;
    private readonly HashSet<string> _expandedTeamIds = new();
    private TeamStatusFilter _teamStatusFilter = TeamStatusFilter.All;

    // Screensaver (kein aktiver Einsatz)
    private System.Threading.Timer? _screensaverClockTimer;
    private DateTime _screensaverNow;
    private string? _pendingTeamScrollId;
    private string? _lastScrolledTeamId;
    private string? _lastCloseMissionRequestToken;

    private bool HasActiveEinsatz =>
        !string.IsNullOrWhiteSpace(EinsatzService.CurrentEinsatz.Einsatzort)
        && EinsatzService.CurrentEinsatz.EinsatzEnde is null;

    protected override void OnParametersSet()
    {
        if (!string.IsNullOrWhiteSpace(OpenCloseMissionAt)
            && !string.Equals(OpenCloseMissionAt, _lastCloseMissionRequestToken, StringComparison.Ordinal))
        {
            _lastCloseMissionRequestToken = OpenCloseMissionAt;
            if (HasActiveEinsatz)
            {
                OpenCloseMissionModal();
            }
        }

        if (!string.IsNullOrWhiteSpace(ScrollToTeamId)
            && IsValidEntityId(ScrollToTeamId)
            && !string.Equals(ScrollToTeamId, _lastScrolledTeamId, StringComparison.Ordinal))
        {
            _pendingTeamScrollId = ScrollToTeamId;
        }
    }

    private static readonly string[] _screensaverTips =
    [
        "Teams können über die Karte visuell verfolgt werden.",
        "QR-Codes ermöglichen Echtzeit-Updates auf Team-Smartphones.",
        "GPS-Halsbänder senden Positionen automatisch an den Server.",
        "Notizen und Funksprüche werden im Einsatzbericht gespeichert.",
        "Suchgebiete können direkt auf der Karte eingezeichnet werden.",
        "Der Einsatzbericht kann als PDF oder Excel exportiert werden.",
        "Archivierte Einsätze können jederzeit eingesehen werden.",
        "Push-Nachrichten erreichen Teams direkt auf ihren Smartphones.",
        "Das Dashboard-Layout kann individuell angepasst werden.",
        "Wetterdaten werden automatisch für den Einsatzort geladen.",
    ];

    private sealed class ClientLocalNowDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
        public int Second { get; set; }
    }

    protected override void OnInitialized()
    {
        EinsatzService.TeamAdded += OnTeamChanged;
        EinsatzService.TeamRemoved += OnTeamChanged;
        EinsatzService.TeamUpdated += OnTeamChanged;
        EinsatzService.EinsatzChanged += OnEinsatzChanged;
        EinsatzService.NoteAdded += OnNoteAdded;
        CollarTrackingService.CollarLocationReceived += OnCollarLocationChanged;
        CollarTrackingService.CollarHistoryCleared += OnCollarHistoryChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        _appSettings = await SettingsService.GetAppSettingsAsync();
        await LoadMasterDataAsync();
        LoadQuickNoteTemplates();
        _currentLayout = await DashboardLayoutService.LoadLayoutAsync(EinsatzService.CurrentEinsatz.Fuehrungsassistent);
        await RefreshMonitorWeatherAsync(forceGeocoding: true);
        StartWeatherRefreshTimer();
        StartDurationRefreshTimer();
        _screensaverNow = TimeService.Now;
        _screensaverClockTimer = new System.Threading.Timer(async _ =>
        {
            _screensaverNow = TimeService.Now;
            await InvokeAsync(StateHasChanged);
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    protected override bool ShouldRender() => true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RefreshEinsatzdauerFromHeaderTimeAsync();
            await InvokeAsync(StateHasChanged);
        }

        if (!string.IsNullOrWhiteSpace(_pendingTeamScrollId))
        {
            await JS.InvokeVoidAsync("layoutTools.scrollToElement", $"team-{_pendingTeamScrollId}");
            _lastScrolledTeamId = _pendingTeamScrollId;
            _pendingTeamScrollId = null;
        }
    }

    public void Dispose()
    {
        EinsatzService.TeamAdded -= OnTeamChanged;
        EinsatzService.TeamRemoved -= OnTeamChanged;
        EinsatzService.TeamUpdated -= OnTeamChanged;
        EinsatzService.EinsatzChanged -= OnEinsatzChanged;
        EinsatzService.NoteAdded -= OnNoteAdded;
        CollarTrackingService.CollarLocationReceived -= OnCollarLocationChanged;
        CollarTrackingService.CollarHistoryCleared -= OnCollarHistoryChanged;
        _weatherRefreshTimer?.Dispose();
        _durationRefreshTimer?.Dispose();
        _screensaverClockTimer?.Dispose();
    }

    // ===== Dashboard-Layout-Methoden =====

    private async Task TogglePanelVisibleAsync(string panelId)
    {
        var panel = _currentLayout.FirstOrDefault(p => p.PanelId == panelId);
        if (panel is null) return;
        panel.IsVisible = !panel.IsVisible;
        await SaveLayoutAsync();
    }

    private async Task SaveLayoutAsync()
    {
        await DashboardLayoutService.SaveLayoutAsync(
            EinsatzService.CurrentEinsatz.Fuehrungsassistent, _currentLayout);
    }

    private static string PanelLabel(string panelId) =>
        KnownPanels.Labels.TryGetValue(panelId, out var label) ? label : panelId;

    // ===== Bestehende Methoden (unverändert) =====

    private IEnumerable<PersonalEntry> ActivePersonnel => _personalList
        .Where(person => person.IsActive)
        .OrderBy(person => person.Nachname)
        .ThenBy(person => person.Vorname);

    private IEnumerable<DogEntry> ActiveDogs => _dogList
        .Where(dog => dog.IsActive)
        .OrderBy(dog => dog.Name);

    private List<string> SelectedPersonnelNames => ActivePersonnel
        .Where(person => _selectedPersonnelIds.Contains(person.Id))
        .Select(person => person.FullName)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name)
        .ToList();

    private List<string> SelectedDogNames => ActiveDogs
        .Where(dog => _selectedDogIds.Contains(dog.Id))
        .Select(dog => dog.Name)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name)
        .ToList();

    private IEnumerable<PersonalEntry> HundefuehrerOptions
    {
        get
        {
            var allHandlers = ActivePersonnel
                .Where(person => person.Skills.HasFlag(PersonalSkills.Hundefuehrer));

            if (!string.IsNullOrWhiteSpace(_teamForm.DogId))
            {
                var selectedDog = _dogList.FirstOrDefault(d => d.Id == _teamForm.DogId);
                if (selectedDog != null && selectedDog.HundefuehrerIds.Count > 0)
                {
                    return allHandlers.Where(p => selectedDog.HundefuehrerIds.Contains(p.Id));
                }
            }

            return allHandlers;
        }
    }

    private IEnumerable<PersonalEntry> HelferOptions => ActivePersonnel
        .Where(person => person.Skills.HasFlag(PersonalSkills.Helfer) || person.Skills.HasFlag(PersonalSkills.Hundefuehrer));

    private IEnumerable<PersonalEntry> DrohnenpilotOptions => ActivePersonnel
        .Where(person => person.Skills.HasFlag(PersonalSkills.Drohnenpilot));

    private IEnumerable<DogEntry> AvailableDogs => _dogList
        .Where(dog => dog.IsActive && (string.IsNullOrWhiteSpace(_teamForm.HundefuehrerId) || dog.HundefuehrerIds.Contains(_teamForm.HundefuehrerId)))
        .OrderBy(dog => dog.Name);

    private IEnumerable<DroneEntry> AvailableDrones => _droneList
        .Where(drone => drone.IsActive)
        .OrderBy(drone => drone.Name)
        .ThenBy(drone => drone.Modell);

    private async Task LoadMasterDataAsync()
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
        var activePersons = _personalList.Where(p => p.IsActive).OrderBy(p => p.FullName).ToList();
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
                "ELW verlaesst Einsatzstelle",
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
                    _monitorWeatherError = "Live-Aktualisierung fehlgeschlagen. Letzte gueltige Wetterdaten werden angezeigt.";
                }

                return;
            }

            _monitorWeather = weather;
            _monitorForecast = forecast;
            _monitorWeatherLoadedAtLocal = DateTime.Now;
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
                _monitorWeatherError = "Wetterdienst derzeit nicht erreichbar. Letzte gueltige Daten bleiben sichtbar.";
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
            _monitorWeatherError = "Kein Einsatzort gesetzt. Wetterdaten koennen nicht geladen werden.";
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

    private static string GetWeatherRiskBadgeClass(WeatherData weather)
    {
        return EvaluateWeatherRisk(weather) switch
        {
            WeatherRiskLevel.Kritisch => "bg-danger",
            WeatherRiskLevel.Erhoeht => "bg-warning text-dark",
            WeatherRiskLevel.Beobachten => "bg-info",
            _ => "bg-success"
        };
    }

    private static string GetWeatherRiskLabel(WeatherData weather)
    {
        return EvaluateWeatherRisk(weather) switch
        {
            WeatherRiskLevel.Kritisch => "Kritisch",
            WeatherRiskLevel.Erhoeht => "Erhoeht",
            WeatherRiskLevel.Beobachten => "Beobachten",
            _ => "Stabil"
        };
    }

    private static string GetForecastRiskClass(WeatherData forecast)
    {
        return EvaluateWeatherRisk(forecast) switch
        {
            WeatherRiskLevel.Kritisch => "forecast-critical",
            WeatherRiskLevel.Erhoeht => "forecast-warning",
            _ => string.Empty
        };
    }

    private static WeatherRiskLevel EvaluateWeatherRisk(WeatherData weather)
    {
        if (weather.WarnungsStufe >= WarnungsStufe.Unwetter
            || weather.Windboeen >= 65
            || weather.Niederschlag >= 8
            || weather.Sichtweite < 1.5)
        {
            return WeatherRiskLevel.Kritisch;
        }

        if (weather.WarnungsStufe >= WarnungsStufe.Markant
            || weather.Windboeen >= 50
            || weather.Niederschlag >= 4
            || weather.Sichtweite < 3)
        {
            return WeatherRiskLevel.Erhoeht;
        }

        if (weather.WarnungsStufe == WarnungsStufe.Vorabwarnung
            || weather.Windboeen >= 35
            || weather.Niederschlag >= 1.5
            || weather.Sichtweite < 5)
        {
            return WeatherRiskLevel.Beobachten;
        }

        return WeatherRiskLevel.Stabil;
    }

    private enum WeatherRiskLevel
    {
        Stabil,
        Beobachten,
        Erhoeht,
        Kritisch
    }

    private enum TeamStatusFilter
    {
        All,
        Running,
        Pausing,
        Ready,
        Critical
    }

    private void SetTeamStatusFilter(TeamStatusFilter filter)
    {
        _teamStatusFilter = filter;
    }

    private IEnumerable<Team> GetFilteredTeams()
    {
        var teams = EinsatzService.Teams.AsEnumerable();
        return _teamStatusFilter switch
        {
            TeamStatusFilter.Running => teams.Where(team => team.IsRunning),
            TeamStatusFilter.Pausing => teams.Where(team => team.IsPausing),
            TeamStatusFilter.Ready => teams.Where(team => !team.IsRunning && !team.IsPausing),
            TeamStatusFilter.Critical => teams.Where(team => team.IsSecondWarning),
            _ => teams
        };
    }

    private int GetTeamCountForFilter(TeamStatusFilter filter)
    {
        return filter switch
        {
            TeamStatusFilter.Running => EinsatzService.Teams.Count(team => team.IsRunning),
            TeamStatusFilter.Pausing => EinsatzService.Teams.Count(team => team.IsPausing),
            TeamStatusFilter.Ready => EinsatzService.Teams.Count(team => !team.IsRunning && !team.IsPausing),
            TeamStatusFilter.Critical => EinsatzService.Teams.Count(team => team.IsSecondWarning),
            _ => EinsatzService.Teams.Count
        };
    }

    private void ToggleTeamExpand(string teamId)
    {
        if (!_expandedTeamIds.Remove(teamId))
            _expandedTeamIds.Add(teamId);
    }

    private CollarLocation? GetLastCollarLocation(Team team)
    {
        if (string.IsNullOrWhiteSpace(team.CollarId)) return null;

        var latest = CollarTrackingService.GetLatestLocation(team.CollarId);
        if (latest is not null)
        {
            return latest;
        }

        var history = CollarTrackingService.GetLocationHistory(team.CollarId);
        return history.Count > 0 ? history[^1] : null;
    }

    private List<GlobalNotesEntry> GetLatestTeamNoteEntries(Team team, int take)
    {
        var teamEntries = EinsatzService.GlobalNotes
            .Where(note =>
                string.Equals(note.SourceTeamId, team.TeamId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(note.SourceTeamName, team.TeamName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(note => note.Timestamp)
            .Take(take)
            .ToList();
        return teamEntries;
    }

    private SearchArea? GetTeamSearchArea(Team team)
    {
        if (string.IsNullOrWhiteSpace(team.SearchAreaId))
        {
            return null;
        }

        return EinsatzService.CurrentEinsatz.SearchAreas.FirstOrDefault(area => area.Id == team.SearchAreaId);
    }

    private List<TeamTrackSnapshot> GetCompletedSearches(Team team, int take)
    {
        return EinsatzService.CurrentEinsatz.TrackSnapshots
            .Where(snapshot => string.Equals(snapshot.TeamId, team.TeamId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .Take(take)
            .ToList();
    }

    private string BuildTeamMapLink(Team team)
    {
        if (!string.IsNullOrWhiteSpace(team.CollarId))
        {
            var encoded = Uri.EscapeDataString(team.CollarId);
            return $"/einsatz-karte?focusCollarId={encoded}";
        }

        return "/einsatz-karte";
    }

    private static string GetTeamColor(Team team)
    {
        if (team.IsDroneTeam)
        {
            return "#0dcaf0";
        }

        if (team.IsSupportTeam)
        {
            return "#6c757d";
        }

        return team.DogSpecialization switch
        {
            DogSpecialization.Flaechensuche => "#4CAF50",
            DogSpecialization.Truemmersuche => "#FF9800",
            DogSpecialization.Wasserortung => "#2196F3",
            DogSpecialization.Mantrailing => "#9C27B0",
            _ => "#1e88e5"
        };
    }

    private string GetAlarmDisplay()
    {
        if (EinsatzService.CurrentEinsatz.AlarmierungsZeit.HasValue)
        {
            return EinsatzService.CurrentEinsatz.AlarmierungsZeit.Value.ToString("dd.MM.yyyy HH:mm");
        }

        if (!string.IsNullOrWhiteSpace(EinsatzService.CurrentEinsatz.Alarmiert))
        {
            return EinsatzService.CurrentEinsatz.Alarmiert;
        }

        return EinsatzService.CurrentEinsatz.EinsatzDatum.ToString("dd.MM.yyyy HH:mm");
    }

    private void OpenCloseMissionModal()
    {
        _showCloseMissionModal = true;
        _closeMissionModalError = string.Empty;
        _closeMissionChecklistConfirmed = false;
        _closeMissionIncludeTracks = false;
        _closeMissionResult = string.Empty;
        _closeMissionRemarks = string.Empty;
        _closeMissionEndTime = TimeOnly.FromDateTime(TimeService.Now);
        _closeMissionStatus = string.Empty;
        _closeMissionStatusIsError = false;

        _selectedPersonnelIds.Clear();
        _selectedDogIds.Clear();
        PreselectVorOrtChecklists();
    }

    private void CloseCloseMissionModal()
    {
        if (_closingMission)
        {
            return;
        }

        _showCloseMissionModal = false;
        _closeMissionModalError = string.Empty;
    }

    private void TogglePersonnelSelection(string personalId, ChangeEventArgs args)
    {
        var isChecked = args.Value switch
        {
            bool b => b,
            string s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "on",
            _ => false
        };

        if (isChecked)
        {
            _selectedPersonnelIds.Add(personalId);
            return;
        }

        _selectedPersonnelIds.Remove(personalId);
    }

    private void ToggleDogSelection(string dogId, ChangeEventArgs args)
    {
        var isChecked = args.Value switch
        {
            bool b => b,
            string s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "on",
            _ => false
        };

        if (isChecked)
        {
            _selectedDogIds.Add(dogId);
            return;
        }

        _selectedDogIds.Remove(dogId);
    }

    private void PreselectVorOrtChecklists()
    {
        var personnelByName = ActivePersonnel
            .Where(person => !string.IsNullOrWhiteSpace(person.FullName))
            .ToDictionary(person => person.FullName.Trim(), person => person.Id, StringComparer.OrdinalIgnoreCase);

        var dogsByName = ActiveDogs
            .Where(dog => !string.IsNullOrWhiteSpace(dog.Name))
            .ToDictionary(dog => dog.Name.Trim(), dog => dog.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var team in EinsatzService.Teams)
        {
            if (!string.IsNullOrWhiteSpace(team.HundefuehrerName)
                && personnelByName.TryGetValue(team.HundefuehrerName.Trim(), out var fuehrerId))
            {
                _selectedPersonnelIds.Add(fuehrerId);
            }

            foreach (var helferName in team.HelferNames)
            {
                if (!string.IsNullOrWhiteSpace(helferName)
                    && personnelByName.TryGetValue(helferName.Trim(), out var helferId))
                {
                    _selectedPersonnelIds.Add(helferId);
                }
            }

            if (!string.IsNullOrWhiteSpace(team.DogName)
                && dogsByName.TryGetValue(team.DogName.Trim(), out var dogId))
            {
                _selectedDogIds.Add(dogId);
            }
        }
    }

    private async Task ConfirmCloseMissionAsync()
    {
        _closeMissionModalError = string.Empty;

        if (string.IsNullOrWhiteSpace(_closeMissionResult))
        {
            _closeMissionModalError = "Bitte ein Ergebnis auswaehlen.";
            return;
        }

        if (!_closeMissionChecklistConfirmed)
        {
            _closeMissionModalError = "Bitte die Pruefung der Ressourcen bestaetigen.";
            return;
        }

        _closingMission = true;

        try
        {
            await EinsatzService.EndEinsatzAsync();

            var snapshot = BuildArchiveSnapshot();
            snapshot.EinsatzEnde = ResolveSelectedEndTime();

            var archived = await ArchivService.ArchiveEinsatzAsync(
                snapshot,
                _closeMissionResult,
                _closeMissionRemarks.Trim(),
                SelectedPersonnelNames,
                SelectedDogNames);

            var pdfExport = await PdfExportService.ExportArchivedEinsatzToPdfAsync(archived, _closeMissionIncludeTracks);

            EinsatzService.ResetEinsatz();
            _showCloseMissionModal = false;

            _closeMissionStatusIsError = !pdfExport.Success;
            _closeMissionStatus = pdfExport.Success
                ? $"Einsatz abgeschlossen. Archiviert als {archived.EinsatzNummer}. PDF erzeugt: {pdfExport.FilePath}"
                : $"Einsatz abgeschlossen und archiviert. PDF konnte nicht erzeugt werden: {pdfExport.ErrorMessage}";

            Navigation.NavigateTo("/einsatz-start", forceLoad: false);
        }
        catch (Exception ex)
        {
            _closeMissionModalError = $"Abschluss fehlgeschlagen: {ex.Message}";
            _closeMissionStatusIsError = true;
            _closeMissionStatus = "Einsatz konnte nicht abgeschlossen werden.";
        }
        finally
        {
            _closingMission = false;
        }
    }

    private DateTime ResolveSelectedEndTime()
    {
        if (_closeMissionEndTime.HasValue)
        {
            var baseDate = TimeService.Now.Date;
            return baseDate.Add(_closeMissionEndTime.Value.ToTimeSpan());
        }

        return TimeService.Now;
    }

    private EinsatzData BuildArchiveSnapshot()
    {
        var current = EinsatzService.CurrentEinsatz;
        return new EinsatzData
        {
            Einsatzleiter = current.Einsatzleiter,
            Fuehrungsassistent = current.Fuehrungsassistent,
            Alarmiert = current.Alarmiert,
            Einsatzort = current.Einsatzort,
            MapAddress = current.MapAddress,
            ExportPfad = current.ExportPfad,
            IstEinsatz = current.IstEinsatz,
            AnzahlTeams = EinsatzService.Teams.Count,
            EinsatzDatum = current.EinsatzDatum,
            EinsatzEnde = current.EinsatzEnde,
            EinsatzNummer = current.EinsatzNummer,
            StaffelName = current.StaffelName,
            StaffelAdresse = current.StaffelAdresse,
            StaffelTelefon = current.StaffelTelefon,
            StaffelEmail = current.StaffelEmail,
            StaffelLogoPfad = current.StaffelLogoPfad,
            AlarmierungsZeit = current.AlarmierungsZeit,
            ElwPosition = current.ElwPosition,
            Teams = EinsatzService.Teams.ToList(),
            SearchAreas = current.SearchAreas.ToList(),
            GlobalNotesEntries = EinsatzService.GlobalNotes.ToList(),
            TrackSnapshots = current.TrackSnapshots.ToList()
        };
    }

    private async Task StartTeamAsync(string teamId)
    {
        var team = EinsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);

        // Kein Halsband zugewiesen → Dialog zur Auswahl einblenden (nur für Hundeteams)
        if (team != null && !team.IsDroneTeam && !team.IsSupportTeam && string.IsNullOrEmpty(team.CollarId))
        {
            _collarSelectTeamId = teamId;
            _collarSelectCollarId = string.Empty;
            _showCollarSelectModal = true;
            return;
        }

        // Bei Suchstart: Halsband-History löschen für frischen Track
        if (team != null && !string.IsNullOrEmpty(team.CollarId))
        {
            CollarTrackingService.ClearCollarHistory(team.CollarId);
        }

        try
        {
            await EinsatzService.StartTeamTimerAsync(teamId);
        }
        catch (InvalidOperationException ex)
        {
            _teamStatusMessage = ex.Message;
            _teamStatusIsError = true;
        }
    }

    private async Task StopTeamAsync(string teamId)
    {
        var team = EinsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
        TeamTrackSnapshot? collarSnapshot = null;
        TeamTrackSnapshot? humanSnapshot = null;

        if (team?.CollarId != null)
        {
            var history = CollarTrackingService.GetLocationHistory(team.CollarId);
            if (history.Count > 0)
            {
                var searchArea = EinsatzService.CurrentEinsatz.SearchAreas
                    .FirstOrDefault(a => a.Id == team.SearchAreaId);
                var color = searchArea?.Color ?? "#FF4444";

                collarSnapshot = new TeamTrackSnapshot
                {
                    CollarId = team.CollarId,
                    CollarName = team.CollarName ?? team.CollarId,
                    TeamId = team.TeamId,
                    TeamName = team.TeamName,
                    SearchAreaName = team.SearchAreaName,
                    Color = color,
                    CapturedAt = DateTime.Now,
                    TrackType = TrackType.CollarTrack,
                    Points = history.Select(loc => new TrackPoint
                    {
                        Latitude = loc.Latitude,
                        Longitude = loc.Longitude,
                        Timestamp = loc.Timestamp
                    }).ToList()
                };
            }
        }

        // Mensch-Laufweg (HumanTrack) aus Telefon-GPS aufzeichnen
        if (team != null)
        {
            var phoneHistory = EinsatzService.GetPhoneTrackHistory(team.TeamId);
            if (phoneHistory.Count >= 2)
            {
                var searchArea = EinsatzService.CurrentEinsatz.SearchAreas
                    .FirstOrDefault(a => a.Id == team.SearchAreaId);
                var humanColor = searchArea?.Color ?? "#1976D2";

                humanSnapshot = new TeamTrackSnapshot
                {
                    CollarId = string.Empty,
                    // CollarName wird hier als generischer Anzeigename im Snapshot verwendet —
                    // für HumanTrack-Einträge enthält dieses Feld den Hundeführer-Namen (oder Team-Namen).
                    CollarName = string.IsNullOrWhiteSpace(team.HundefuehrerName) ? team.TeamName : team.HundefuehrerName,
                    TeamId = team.TeamId,
                    TeamName = team.TeamName,
                    SearchAreaName = team.SearchAreaName,
                    Color = humanColor,
                    CapturedAt = DateTime.Now,
                    TrackType = Einsatzueberwachung.Domain.Models.Enums.TrackType.HumanTrack,
                    Points = phoneHistory.Select(loc => new TrackPoint
                    {
                        Latitude = loc.Latitude,
                        Longitude = loc.Longitude,
                        Timestamp = loc.Timestamp
                    }).ToList()
                };
            }
        }

        // Collar- und Human-Track derselben Team-Suche in eine gemeinsame Episode gruppieren.
        if (team != null && (collarSnapshot != null || humanSnapshot != null))
        {
            var starts = new List<DateTime>();
            var ends = new List<DateTime>();

            if (collarSnapshot != null)
            {
                starts.Add(collarSnapshot.Points.Count > 0 ? collarSnapshot.Points[0].Timestamp : collarSnapshot.CapturedAt);
                ends.Add(collarSnapshot.Points.Count > 0 ? collarSnapshot.Points[^1].Timestamp : collarSnapshot.CapturedAt);
            }

            if (humanSnapshot != null)
            {
                starts.Add(humanSnapshot.Points.Count > 0 ? humanSnapshot.Points[0].Timestamp : humanSnapshot.CapturedAt);
                ends.Add(humanSnapshot.Points.Count > 0 ? humanSnapshot.Points[^1].Timestamp : humanSnapshot.CapturedAt);
            }

            var search = await EinsatzService.CreateCompletedSearchAsync(
                team.TeamId,
                starts.Min(),
                ends.Max(),
                team.SearchAreaId);

            if (collarSnapshot != null)
            {
                await EinsatzService.AddTrackToCompletedSearchAsync(search.Id, collarSnapshot);

                // Live-Track von der Karte entfernen — der Snapshot-Track ersetzt ihn
                CollarTrackingService.ClearCollarHistory(collarSnapshot.CollarId);
            }

            if (humanSnapshot != null)
            {
                await EinsatzService.AddTrackToCompletedSearchAsync(search.Id, humanSnapshot);
                EinsatzService.ClearPhoneTrackHistory(team.TeamId);
            }
        }

        // Halsband nach dem Stopp automatisch freigeben (übergabe an nächstes Team möglich)
        if (team?.CollarId != null)
        {
            await CollarTrackingService.UnassignCollarAsync(team.CollarId);
        }

        await EinsatzService.StopTeamTimerAsync(teamId);
    }

    private async Task ResetTeamAsync(string teamId)
    {
        await EinsatzService.ResetTeamTimerAsync(teamId);
    }

    // --- Halsband-Auswahl Modal (wird gezeigt wenn beim Start kein Halsband zugewiesen ist) ---

    private async Task CollarSelectConfirmAsync()
    {
        if (_collarSelectTeamId == null) return;

        if (!string.IsNullOrEmpty(_collarSelectCollarId))
        {
            await CollarTrackingService.AssignCollarToTeamAsync(_collarSelectCollarId, _collarSelectTeamId);
        }

        _showCollarSelectModal = false;

        // Jetzt Start durchführen (Halsband ist jetzt zugewiesen oder bewusst ausgelassen)
        var team = EinsatzService.Teams.FirstOrDefault(t => t.TeamId == _collarSelectTeamId);
        if (team != null && !string.IsNullOrEmpty(team.CollarId))
        {
            CollarTrackingService.ClearCollarHistory(team.CollarId);
        }

        try
        {
            await EinsatzService.StartTeamTimerAsync(_collarSelectTeamId);
        }
        catch (InvalidOperationException ex)
        {
            _teamStatusMessage = ex.Message;
            _teamStatusIsError = true;
        }

        _collarSelectTeamId = null;
        _collarSelectCollarId = string.Empty;
    }

    private async Task CollarSelectStartWithoutAsync()
    {
        if (_collarSelectTeamId == null) return;
        _showCollarSelectModal = false;

        try
        {
            await EinsatzService.StartTeamTimerAsync(_collarSelectTeamId);
        }
        catch (InvalidOperationException ex)
        {
            _teamStatusMessage = ex.Message;
            _teamStatusIsError = true;
        }

        _collarSelectTeamId = null;
        _collarSelectCollarId = string.Empty;
    }

    private void CollarSelectCancel()
    {
        _showCollarSelectModal = false;
        _collarSelectTeamId = null;
        _collarSelectCollarId = string.Empty;
    }

    private Task RequestResetTeamAsync(string teamId)
    {
        var team = EinsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
        if (team is not null && team.IsPausing && !team.IsPauseComplete)
        {
            _pauseResetTeamId = teamId;
            _showPauseResetModal = true;
        }
        else
        {
            _ = ResetTeamAsync(teamId);
        }
        return Task.CompletedTask;
    }

    private async Task ConfirmResetTeamAsync()
    {
        _showPauseResetModal = false;
        if (_pauseResetTeamId is not null)
        {
            await ResetTeamAsync(_pauseResetTeamId);
            _pauseResetTeamId = null;
        }
    }

    private void ClosePauseResetModal()
    {
        _showPauseResetModal = false;
        _pauseResetTeamId = null;
    }

    private void EditTeam(string teamId)
    {
        var team = EinsatzService.Teams.FirstOrDefault(entry => entry.TeamId == teamId);
        if (team == null)
        {
            return;
        }

        _editingTeamId = team.TeamId;
        _teamForm.TeamName = team.TeamName;
        _teamForm.TeamType = team.IsDroneTeam ? "drone" : team.IsSupportTeam ? "support" : "dog";
        _teamForm.DogId = team.DogId;
        _teamForm.DroneId = team.DroneId;
        _teamForm.HundefuehrerId = team.HundefuehrerId;
        _teamForm.HelferId = team.HelferIds.ElementAtOrDefault(0) ?? string.Empty;
        _teamForm.HelferId2 = team.HelferIds.ElementAtOrDefault(1) ?? string.Empty;
        _teamForm.HelferId3 = team.HelferIds.ElementAtOrDefault(2) ?? string.Empty;
        _teamForm.SearchAreaId = team.SearchAreaId;
        _teamForm.CollarId = team.CollarId ?? string.Empty;
        _teamForm.FirstWarningMinutes = team.FirstWarningMinutes;
        _teamForm.SecondWarningMinutes = team.SecondWarningMinutes;
        _teamForm.Notes = team.Notes;
        _showTeamModal = true;
        ClearTeamFormMessage();
    }

    private async Task SaveTeamAsync()
    {
        ClearTeamFormMessage();

        if (!ValidateTeamForm(out var validationMessage))
        {
            SetTeamFormMessage(validationMessage, true);
            return;
        }

        if (_editingTeamId is null)
        {
            var newTeam = BuildNewTeam();
            await EinsatzService.AddTeamAsync(newTeam);
            await SyncTeamSearchAreaAsync(newTeam, string.Empty);
            await SyncCollarAssignmentAsync(newTeam);

            SetTeamFormMessage("Team wurde gespeichert.", false);
            ResetTeamForm();
            _showTeamModal = false;
            return;
        }

        var existingTeam = await EinsatzService.GetTeamByIdAsync(_editingTeamId);
        if (existingTeam == null)
        {
            SetTeamFormMessage("Das zu bearbeitende Team wurde nicht gefunden.", true);
            return;
        }

        var previousAreaId = existingTeam.SearchAreaId;
        var previousCollarId = existingTeam.CollarId;
        ApplyFormToExistingTeam(existingTeam);
        await EinsatzService.UpdateTeamAsync(existingTeam);
        await SyncTeamSearchAreaAsync(existingTeam, previousAreaId);
        await SyncCollarAssignmentAsync(existingTeam, previousCollarId);
        await EinsatzService.AddGlobalNoteWithSourceAsync(
            $"{existingTeam.TeamName} wurde aktualisiert.",
            existingTeam.TeamId,
            existingTeam.TeamName,
            "Team",
            GlobalNotesEntryType.System,
            "Einsatzleitung");

        SetTeamFormMessage("Team wurde aktualisiert.", false);
        ResetTeamForm();
        _showTeamModal = false;
    }

    private async Task SyncTeamSearchAreaAsync(Team team, string previousAreaId)
    {
        if (!string.IsNullOrWhiteSpace(previousAreaId) && previousAreaId != _teamForm.SearchAreaId)
        {
            await EinsatzService.AssignTeamToSearchAreaAsync(previousAreaId, string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(_teamForm.SearchAreaId))
        {
            await EinsatzService.AssignTeamToSearchAreaAsync(_teamForm.SearchAreaId, team.TeamId);
        }
        else
        {
            team.SearchAreaId = string.Empty;
            team.SearchAreaName = string.Empty;
            await EinsatzService.UpdateTeamAsync(team);
        }
    }

    private async Task SyncCollarAssignmentAsync(Team team, string? previousCollarId = null)
    {
        if (!string.IsNullOrWhiteSpace(previousCollarId) && previousCollarId != _teamForm.CollarId)
        {
            await CollarTrackingService.UnassignCollarAsync(previousCollarId);
        }

        if (!string.IsNullOrWhiteSpace(_teamForm.CollarId))
        {
            if (_teamForm.CollarId != previousCollarId)
            {
                await CollarTrackingService.AssignCollarToTeamAsync(_teamForm.CollarId, team.TeamId);
            }
        }
        else if (!string.IsNullOrWhiteSpace(previousCollarId))
        {
            team.CollarId = null;
            team.CollarName = null;
            await EinsatzService.UpdateTeamAsync(team);
        }
    }

    private bool ValidateTeamForm(out string validationMessage)
    {
        if (string.IsNullOrWhiteSpace(_teamForm.TeamName))
        {
            validationMessage = "Bitte einen Teamnamen vergeben.";
            return false;
        }

        if (_teamForm.FirstWarningMinutes <= 0 || _teamForm.SecondWarningMinutes <= 0)
        {
            validationMessage = "Warnschwellen muessen groesser als 0 sein.";
            return false;
        }

        if (_teamForm.SecondWarningMinutes <= _teamForm.FirstWarningMinutes)
        {
            validationMessage = "Warnung 2 muss groesser als Warnung 1 sein.";
            return false;
        }

        if (_teamForm.TeamType == "dog")
        {
            if (string.IsNullOrWhiteSpace(_teamForm.DogId))
            {
                validationMessage = "Bitte einen Hund auswaehlen.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_teamForm.HundefuehrerId))
            {
                validationMessage = "Bitte einen Hundefuehrer auswaehlen.";
                return false;
            }
        }

        if (_teamForm.TeamType == "drone")
        {
            if (string.IsNullOrWhiteSpace(_teamForm.DroneId))
            {
                validationMessage = "Bitte eine Drohne auswaehlen.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_teamForm.HundefuehrerId))
            {
                validationMessage = "Bitte einen Piloten auswaehlen.";
                return false;
            }
        }

        if (_teamForm.TeamType == "support" && string.IsNullOrWhiteSpace(_teamForm.HundefuehrerId))
        {
            validationMessage = "Bitte mindestens eine Person zuordnen.";
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    private Team BuildNewTeam()
    {
        var team = new Team();
        ApplyFormValues(team);
        return team;
    }

    private void ApplyFormToExistingTeam(Team team)
    {
        ApplyFormValues(team);
    }

    private void ApplyFormValues(Team team)
    {
        var resolvedTeamName = ResolveTeamNameFromForm();
        team.TeamName = resolvedTeamName;
        team.HundefuehrerId = _teamForm.HundefuehrerId;
        ApplyHelfersToTeam(team);
        team.DogId = string.Empty;
        team.DogName = string.Empty;
        team.DogSpecialization = DogSpecialization.None;
        team.DroneId = string.Empty;
        team.DroneType = string.Empty;
        team.IsDroneTeam = _teamForm.TeamType == "drone";
        team.IsSupportTeam = _teamForm.TeamType == "support";
        team.FirstWarningMinutes = _teamForm.FirstWarningMinutes;
        team.SecondWarningMinutes = _teamForm.SecondWarningMinutes;
        team.Notes = _teamForm.Notes.Trim();

        var primaryPerson = _personalList.FirstOrDefault(person => person.Id == _teamForm.HundefuehrerId);
        team.HundefuehrerName = primaryPerson?.FullName ?? string.Empty;

        if (_teamForm.TeamType == "dog")
        {
            var dog = _dogList.FirstOrDefault(entry => entry.Id == _teamForm.DogId);
            team.DogId = dog?.Id ?? string.Empty;
            team.DogName = dog?.Name ?? string.Empty;
            team.DogSpecialization = dog?.Specializations ?? DogSpecialization.None;
        }
        else if (_teamForm.TeamType == "drone")
        {
            var drone = _droneList.FirstOrDefault(entry => entry.Id == _teamForm.DroneId);
            team.DroneId = drone?.Id ?? string.Empty;
            team.DroneType = drone?.DisplayName ?? string.Empty;
        }
    }

    private void ApplyHelfersToTeam(Team team)
    {
        var ids = new[] { _teamForm.HelferId, _teamForm.HelferId2, _teamForm.HelferId3 };
        team.HelferIds.Clear();
        team.HelferNames.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!seen.Add(id)) continue;
            var person = _personalList.FirstOrDefault(p => p.Id == id);
            team.HelferIds.Add(id);
            team.HelferNames.Add(person?.FullName ?? string.Empty);
        }
    }

    private async Task OnTeamTypeChangedAsync()
    {
        _teamForm.DogId = string.Empty;
        _teamForm.DroneId = string.Empty;
        _teamForm.HelferId = string.Empty;
        _teamForm.HelferId2 = string.Empty;
        _teamForm.HelferId3 = string.Empty;

        if (_teamForm.TeamType == "support")
        {
            _teamForm.SearchAreaId = string.Empty;
        }

        SyncTeamNameFromSelection();

        await InvokeAsync(StateHasChanged);
    }

    private async Task OnDogChangedAsync()
    {
        var dog = _dogList.FirstOrDefault(entry => entry.Id == _teamForm.DogId);
        if (dog != null)
        {
            if (string.IsNullOrWhiteSpace(_teamForm.HundefuehrerId)
                || !dog.HundefuehrerIds.Contains(_teamForm.HundefuehrerId))
            {
                _teamForm.HundefuehrerId = dog.PrimaryHundefuehrerId;
            }
        }

        SyncTeamNameFromSelection();

        await InvokeAsync(StateHasChanged);
    }

    private async Task OnDroneChangedAsync()
    {
        SyncTeamNameFromSelection();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnHandlerChangedAsync()
    {
        if (!string.IsNullOrWhiteSpace(_teamForm.DogId) && AvailableDogs.All(dog => dog.Id != _teamForm.DogId))
        {
            _teamForm.DogId = string.Empty;
            SyncTeamNameFromSelection();
        }

        await InvokeAsync(StateHasChanged);
    }

    private void SyncTeamNameFromSelection()
    {
        _teamForm.TeamName = ResolveTeamNameFromForm();
    }

    private string ResolveTeamNameFromForm()
    {
        if (_teamForm.TeamType == "dog")
        {
            var dogName = _dogList.FirstOrDefault(entry => entry.Id == _teamForm.DogId)?.Name;
            return dogName?.Trim() ?? string.Empty;
        }

        if (_teamForm.TeamType == "drone")
        {
            var droneName = _droneList.FirstOrDefault(entry => entry.Id == _teamForm.DroneId)?.DisplayName;
            return droneName?.Trim() ?? string.Empty;
        }

        return _teamForm.TeamName.Trim();
    }

    private async Task AddGlobalNoteAsync()
    {
        if (string.IsNullOrWhiteSpace(_newNoteText))
        {
            return;
        }

        var type = _newNoteType == "Funk" ? "Funk" : "Notiz";
        var (sourceId, sourceName, createdBy) = ResolveSelectedSource(_newNoteSourceId);
        await EinsatzService.AddGlobalNoteWithSourceAsync(
            _newNoteText.Trim(),
            sourceId,
            sourceName,
            type,
            GlobalNotesEntryType.Manual,
            createdBy);

        _newNoteText = string.Empty;
        _isNoteFormExpanded = false;
    }

    private async Task OpenNoteFormAsync()
    {
        _isNoteFormExpanded = true;
        await InvokeAsync(StateHasChanged);
        await JS.InvokeVoidAsync("eval", "setTimeout(()=>document.querySelector('#note-expander-form textarea')?.focus(),50)");
    }

    private void CloseNoteForm()
    {
        _isNoteFormExpanded = false;
        _newNoteText = string.Empty;
    }

    private async Task AddQuickNoteAsync(string shortText)
    {
        if (string.IsNullOrWhiteSpace(shortText))
        {
            return;
        }

        var selectedSource = string.IsNullOrWhiteSpace(_newNoteSourceId) ? "einsatzleitung" : _newNoteSourceId;
        var (sourceId, sourceName, createdBy) = ResolveSelectedSource(selectedSource);
        var noteText = $"[{DateTime.Now:HH:mm}] {shortText.Trim()}";

        await EinsatzService.AddGlobalNoteWithSourceAsync(
            noteText,
            sourceId,
            sourceName,
            "Notiz",
            GlobalNotesEntryType.Manual,
            createdBy);

        _isNoteFormExpanded = false;
    }

    private async Task AddReplyAsync(string noteId)
    {
        _replyTexts.TryGetValue(noteId, out var text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var sourceId = GetReplySourceId(noteId);
        var (resolvedSourceId, sourceName, createdBy) = ResolveSelectedSource(sourceId);

        await EinsatzService.AddReplyToNoteAsync(
            noteId,
            text.Trim(),
            resolvedSourceId,
            sourceName,
            createdBy);

        _replyTexts[noteId] = string.Empty;
    }

    private async Task ToggleHistoryAsync(string noteId)
    {
        var show = !_historyVisible.TryGetValue(noteId, out var isVisible) || !isVisible;
        _historyVisible[noteId] = show;

        if (show)
        {
            _historyCache[noteId] = await EinsatzService.GetNoteHistoryAsync(noteId);
        }
    }

    private bool IsHistoryVisible(string noteId)
    {
        return _historyVisible.TryGetValue(noteId, out var value) && value;
    }

    private List<GlobalNotesHistory> GetHistoryEntries(string noteId)
    {
        return _historyCache.TryGetValue(noteId, out var entries) ? entries : new List<GlobalNotesHistory>();
    }

    private string GetReplyText(string noteId)
    {
        return _replyTexts.TryGetValue(noteId, out var text) ? text : string.Empty;
    }

    private string GetReplySourceId(string noteId)
    {
        if (!_replySourceIds.TryGetValue(noteId, out var sourceId) || string.IsNullOrWhiteSpace(sourceId))
        {
            _replySourceIds[noteId] = "einsatzleitung";
        }

        return _replySourceIds[noteId];
    }

    private void SetReplyText(string noteId, string? text)
    {
        _replyTexts[noteId] = text ?? string.Empty;
    }

    private void SetReplySourceId(string noteId, string? sourceId)
    {
        _replySourceIds[noteId] = string.IsNullOrWhiteSpace(sourceId) ? "einsatzleitung" : sourceId;
    }

    private (string SourceId, string SourceName, string CreatedBy) ResolveSelectedSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId == "einsatzleitung")
        {
            return ("einsatzleitung", "Einsatzleitung", "Einsatzleitung");
        }

        var selectedTeam = EinsatzService.Teams.FirstOrDefault(team => team.TeamId == sourceId);
        if (selectedTeam is null)
        {
            return ("einsatzleitung", "Einsatzleitung", "Einsatzleitung");
        }

        return (selectedTeam.TeamId, selectedTeam.TeamName, selectedTeam.TeamName);
    }

    private static string GetNoteCssClass(GlobalNotesEntry note)
    {
        return note.Type switch
        {
            GlobalNotesEntryType.TeamStart => "note-type-teamstart",
            GlobalNotesEntryType.TeamStop => "note-type-teamstop",
            GlobalNotesEntryType.TeamReset => "note-type-teamreset",
            GlobalNotesEntryType.TeamWarning => "note-type-teamwarning",
            GlobalNotesEntryType.EinsatzUpdate => "note-type-einsatzupdate",
            _ => string.Empty
        };
    }

    private void OpenNewTeamModal()
    {
        ResetTeamForm();
        ClearTeamFormMessage();
        _showTeamModal = true;
    }

    private void CloseTeamModal()
    {
        _showTeamModal = false;
        CancelTeamEdit();
    }

    private void CancelTeamEdit()
    {
        ResetTeamForm();
        ClearTeamFormMessage();
    }

    private void ResetTeamForm()
    {
        _editingTeamId = null;
        _teamForm.TeamType = "dog";
        _teamForm.TeamName = string.Empty;
        _teamForm.DogId = string.Empty;
        _teamForm.DroneId = string.Empty;
        _teamForm.HundefuehrerId = string.Empty;
        _teamForm.HelferId = string.Empty;
        _teamForm.HelferId2 = string.Empty;
        _teamForm.HelferId3 = string.Empty;
        _teamForm.SearchAreaId = string.Empty;
        _teamForm.CollarId = string.Empty;
        _teamForm.FirstWarningMinutes = _appSettings.DefaultFirstWarningMinutes;
        _teamForm.SecondWarningMinutes = _appSettings.DefaultSecondWarningMinutes;
        _teamForm.Notes = string.Empty;
    }

    private void SetTeamFormMessage(string message, bool isError)
    {
        _teamFormMessage = message;
        _teamFormIsError = isError;
    }

    private void ClearTeamFormMessage()
    {
        _teamFormMessage = string.Empty;
        _teamFormIsError = false;
    }

    private sealed class TeamEditorModel
    {
        public string TeamType { get; set; } = "dog";
        public string TeamName { get; set; } = string.Empty;
        public string DogId { get; set; } = string.Empty;
        public string DroneId { get; set; } = string.Empty;
        public string HundefuehrerId { get; set; } = string.Empty;
        public string HelferId { get; set; } = string.Empty;
        public string HelferId2 { get; set; } = string.Empty;
        public string HelferId3 { get; set; } = string.Empty;
        public string SearchAreaId { get; set; } = string.Empty;
        public string CollarId { get; set; } = string.Empty;
        public int FirstWarningMinutes { get; set; } = 45;
        public int SecondWarningMinutes { get; set; } = 60;
        public string Notes { get; set; } = string.Empty;
    }

    private sealed class EditEinsatzForm
    {
        public bool IstEinsatz { get; set; } = true;
        public string EinsatzNummer { get; set; } = string.Empty;
        public string AlarmierungsZeit { get; set; } = string.Empty;
        public string Einsatzort { get; set; } = string.Empty;
        public string MapAddress { get; set; } = string.Empty;
        public string Alarmiert { get; set; } = string.Empty;
        public int AnzahlTeams { get; set; }
        public string Einsatzleiter { get; set; } = string.Empty;
        public string Fuehrungsassistent { get; set; } = string.Empty;
        public string Bemerkungen { get; set; } = string.Empty;
    }

    private void OpenEditEinsatzModal()
    {
        var e = EinsatzService.CurrentEinsatz;
        _editEinsatzForm.IstEinsatz = e.IstEinsatz;
        _editEinsatzForm.EinsatzNummer = e.EinsatzNummer;
        _editEinsatzForm.AlarmierungsZeit = e.AlarmierungsZeit?.ToString("HH:mm") ?? string.Empty;
        _editEinsatzForm.Einsatzort = e.Einsatzort;
        _editEinsatzForm.MapAddress = e.MapAddress;
        _editEinsatzForm.Alarmiert = e.Alarmiert;
        _editEinsatzForm.AnzahlTeams = e.AnzahlTeams;
        _editEinsatzForm.Einsatzleiter = e.Einsatzleiter;
        _editEinsatzForm.Fuehrungsassistent = e.Fuehrungsassistent;
        _editEinsatzForm.Bemerkungen = e.ExportPfad;
        _editEinsatzMessage = string.Empty;
        _editEinsatzIsError = false;
        _showEditEinsatzModal = true;
    }

    private void CloseEditEinsatzModal()
    {
        _showEditEinsatzModal = false;
    }

    private async Task SaveEditEinsatzAsync()
    {
        if (string.IsNullOrWhiteSpace(_editEinsatzForm.Einsatzort))
        {
            _editEinsatzMessage = "Einsatzort ist erforderlich.";
            _editEinsatzIsError = true;
            return;
        }

        var current = EinsatzService.CurrentEinsatz;
        var prevFa = current.Fuehrungsassistent;

        DateTime? newAlarmierungsZeit = current.AlarmierungsZeit;
        if (!string.IsNullOrWhiteSpace(_editEinsatzForm.AlarmierungsZeit)
            && TimeOnly.TryParse(_editEinsatzForm.AlarmierungsZeit, out var alarmTime))
        {
            var baseDate = current.AlarmierungsZeit ?? current.EinsatzDatum;
            newAlarmierungsZeit = new DateTime(baseDate.Year, baseDate.Month, baseDate.Day,
                alarmTime.Hour, alarmTime.Minute, 0);
        }

        var updated = new EinsatzData
        {
            IstEinsatz = _editEinsatzForm.IstEinsatz,
            EinsatzNummer = _editEinsatzForm.EinsatzNummer,
            Einsatzort = _editEinsatzForm.Einsatzort,
            MapAddress = _editEinsatzForm.MapAddress,
            Alarmiert = _editEinsatzForm.Alarmiert,
            AlarmierungsZeit = newAlarmierungsZeit,
            AnzahlTeams = _editEinsatzForm.AnzahlTeams,
            ExportPfad = _editEinsatzForm.Bemerkungen,
            Einsatzleiter = _editEinsatzForm.Einsatzleiter,
            Fuehrungsassistent = _editEinsatzForm.Fuehrungsassistent
        };

        await EinsatzService.UpdateEinsatzAsync(updated);

        // Wenn FA gewechselt wurde, gespeichertes Layout des neuen FA laden
        if (!string.Equals(updated.Fuehrungsassistent, prevFa, StringComparison.OrdinalIgnoreCase))
        {
            _currentLayout = await DashboardLayoutService.LoadLayoutAsync(updated.Fuehrungsassistent);
        }

        _showEditEinsatzModal = false;
    }

    private void OpenVermisstenModalNew()
    {
        _vForm = new Einsatzueberwachung.Domain.Models.VermisstenInfo { Id = Guid.NewGuid() };
        _vermisstenMessage = string.Empty;
        _showVermisstenModal = true;
    }

    private void OpenVermisstenModal()
    {
        var list = EinsatzService.CurrentEinsatz.Vermisste ?? new();
        var existing = _vermisstenPanelSelectedId.HasValue
            ? list.FirstOrDefault(v => v.Id == _vermisstenPanelSelectedId.Value)
            : list.FirstOrDefault();

        _vForm = existing is not null
            ? new Einsatzueberwachung.Domain.Models.VermisstenInfo
            {
                Id = existing.Id,
                Vorname = existing.Vorname,
                Nachname = existing.Nachname,
                Alter = existing.Alter,
                Geburtsdatum = existing.Geburtsdatum,
                Kleidung = existing.Kleidung,
                Besonderheiten = existing.Besonderheiten,
                ZuletztGesehenOrt = existing.ZuletztGesehenOrt,
                ZuletztGesehenZeit = existing.ZuletztGesehenZeit,
                ZuletztGesehenVon = existing.ZuletztGesehenVon,
                Vorerkrankungen = existing.Vorerkrankungen,
                Medikamente = existing.Medikamente,
                Orientierung = existing.Orientierung,
                Mobilitaet = existing.Mobilitaet,
                Suizidrisiko = existing.Suizidrisiko,
                Bewaffnet = existing.Bewaffnet,
                PolizeiKontaktName = existing.PolizeiKontaktName,
                PolizeiDienstnummer = existing.PolizeiDienstnummer,
                PolizeiTelefon = existing.PolizeiTelefon,
                PolizeiVermisstenmeldungAufgenommen = existing.PolizeiVermisstenmeldungAufgenommen,
                PolizeiKoordinationBesprochen = existing.PolizeiKoordinationBesprochen,
                PolizeiSuchabschnittAbgestimmt = existing.PolizeiSuchabschnittAbgestimmt,
                PolizeiRueckmeldepflichtVereinbart = existing.PolizeiRueckmeldepflichtVereinbart,
                PolizeiDatenschutzGeklaert = existing.PolizeiDatenschutzGeklaert,
                BosEinheit = existing.BosEinheit,
                BosZugfuehrer = existing.BosZugfuehrer,
                BosFunkrufname = existing.BosFunkrufname,
                BosAufgabenteilung = existing.BosAufgabenteilung,
                BosAbschnittAbgestimmt = existing.BosAbschnittAbgestimmt,
                BosRessourcenBesprochen = existing.BosRessourcenBesprochen
            }
            : new Einsatzueberwachung.Domain.Models.VermisstenInfo { Id = Guid.NewGuid() };

        _vermisstenMessage = string.Empty;
        _showVermisstenModal = true;
    }

    private void CloseVermisstenModal() => _showVermisstenModal = false;

    private void OnVFormGeburtsdatumChanged(ChangeEventArgs e)
    {
        var value = e.Value?.ToString() ?? string.Empty;
        _vForm.Geburtsdatum = value;
        if (DateTime.TryParseExact(value, "dd.MM.yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var birth))
        {
            var today = DateTime.Today;
            var age = today.Year - birth.Year;
            if (birth.Date > today.AddYears(-age)) age--;
            _vForm.Alter = age.ToString();
        }
    }

    private async Task SaveVermisstenAsync()
    {
        await EinsatzService.UpsertVermisstenAsync(_vForm);
        _vermisstenPanelSelectedId = _vForm.Id;
        _vermisstenMessage = "Gespeichert.";
        _vermisstenIsError = false;
        await Task.Delay(1200);
        _showVermisstenModal = false;
    }

    private async Task DeleteCurrentVermisstenAsync()
    {
        if (!_vermisstenPanelSelectedId.HasValue) return;
        await EinsatzService.RemoveVermisstenAsync(_vermisstenPanelSelectedId.Value);
        _vermisstenPanelSelectedId = null;
    }

    private IEnumerable<(string Label, Func<bool> Getter, Action<bool> Setter)> GetPolizeiChecklist()
    {
        yield return ("Vermisstenmeldung aufgenommen",
            () => _vForm.PolizeiVermisstenmeldungAufgenommen,
            v => _vForm.PolizeiVermisstenmeldungAufgenommen = v);
        yield return ("Einsatzkoordination besprochen",
            () => _vForm.PolizeiKoordinationBesprochen,
            v => _vForm.PolizeiKoordinationBesprochen = v);
        yield return ("Suchabschnitte abgestimmt",
            () => _vForm.PolizeiSuchabschnittAbgestimmt,
            v => _vForm.PolizeiSuchabschnittAbgestimmt = v);
        yield return ("Rückmeldepflicht vereinbart",
            () => _vForm.PolizeiRueckmeldepflichtVereinbart,
            v => _vForm.PolizeiRueckmeldepflichtVereinbart = v);
        yield return ("Datenschutz (Personendaten) geklärt",
            () => _vForm.PolizeiDatenschutzGeklaert,
            v => _vForm.PolizeiDatenschutzGeklaert = v);
    }

    /// <summary>
    /// Validates that an ID coming from a URL query parameter only contains characters
    /// expected in a GUID (hex digits and hyphens) before it is forwarded to JavaScript.
    /// </summary>
    private static bool IsValidEntityId(string id) =>
        id.Length <= 64 && id.AsSpan().IndexOfAnyExcept("0123456789abcdefABCDEF-".AsSpan()) == -1;
}

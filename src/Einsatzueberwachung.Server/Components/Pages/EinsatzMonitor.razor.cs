using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;
using Einsatzueberwachung.Server.Components;
using Einsatzueberwachung.Server.Components.Monitor;
using Einsatzueberwachung.Server.Components.Shared;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{
    [Inject] IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] IMasterDataService MasterDataService { get; set; } = default!;
    [Inject] IWeatherService WeatherService { get; set; } = default!;
    [Inject] EinsatzAbschlussService EinsatzAbschluss { get; set; } = default!;
    [Inject] TeamSucheService TeamSuche { get; set; } = default!;
    [CascadingParameter(Name = "LeitungsGeraet")] private string? Geraet { get; set; }
    [Inject] ISettingsService SettingsService { get; set; } = default!;
    [Inject] ITimeService TimeService { get; set; } = default!;
    [Inject] ICollarTrackingService CollarTrackingService { get; set; } = default!;
    [Inject] IDashboardLayoutService DashboardLayoutService { get; set; } = default!;
    [Inject] MissionTopbarService MissionTopbar { get; set; } = default!;
    [Inject] IJSRuntime JS { get; set; } = default!;
    [Inject] NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "scrollTo")]
    public string? ScrollToTeamId { get; set; }

    [SupplyParameterFromQuery(Name = "openCloseMissionAt")]
    public string? OpenCloseMissionAt { get; set; }

    private readonly TeamEditorModel _teamForm = new();
    private string? _editingTeamId;
    private string _teamFormMessage = string.Empty;
    private bool _teamFormIsError;
    private string _teamStatusMessage = string.Empty;
    private bool _teamStatusIsError;
    private bool _showTeamModal;
    private bool _showCloseMissionModal;
    private bool _showPauseResetModal;
    private bool _showCoordinationModal;
    private bool _showEditEinsatzModal;
    private bool _coordinationUpdateInProgress;
    private bool _showVermisstenModal;
    private string? _pendingVermisstenModalSectionScrollTarget;
    private string _vermisstenMessage = string.Empty;
    private bool _vermisstenIsError;
    private Einsatzueberwachung.Domain.Models.VermisstenInfo _vForm = new();

    private bool _einsatzInfoDetailsExpanded;
    private bool _wetterDetailsExpanded;
    private bool _vermisstenDetailsExpanded;
    private Guid? _vermisstenPanelSelectedId;

    // Halsband-Auswahl beim Start (falls noch kein Halsband zugewiesen)
    private bool _showCollarSelectModal;
    private string? _collarSelectTeamId;
    private string _collarSelectCollarId = string.Empty;
    private string _editEinsatzMessage = string.Empty;
    private bool _editEinsatzIsError;
    private MonitorEditEinsatzForm _editEinsatzForm = new();
    private string? _pauseResetTeamId;
    private bool _closingMission;
    private readonly MonitorCloseMissionForm _closeMissionForm = new();
    private int _closeMissionPunktCount;
    private bool _closeMissionChecklistConfirmed
    {
        get => _closeMissionForm.ChecklistConfirmed;
        set => _closeMissionForm.ChecklistConfirmed = value;
    }

    private bool _closeMissionIncludeTracks
    {
        get => _closeMissionForm.IncludeTracks;
        set => _closeMissionForm.IncludeTracks = value;
    }

    private bool _closeMissionIncludePunkte
    {
        get => _closeMissionForm.IncludePoints;
        set => _closeMissionForm.IncludePoints = value;
    }

    private Einsatzueberwachung.Domain.Models.Enums.MapTileType _closeMissionKartenart
    {
        get => _closeMissionForm.MapTileType;
        set => _closeMissionForm.MapTileType = value;
    }

    private string _closeMissionResult
    {
        get => _closeMissionForm.Result;
        set => _closeMissionForm.Result = value;
    }

    private TimeOnly? _closeMissionEndTime
    {
        get => _closeMissionForm.EndTime;
        set => _closeMissionForm.EndTime = value;
    }

    private string _closeMissionRemarks
    {
        get => _closeMissionForm.Remarks;
        set => _closeMissionForm.Remarks = value;
    }

    private string _closeMissionModalError = string.Empty;
    private string _closeMissionStatus = string.Empty;
    private bool _closeMissionStatusIsError;
    private int _closeMissionTrackCount => EinsatzService.CurrentEinsatz.TrackSnapshots?.Count ?? 0;
    private static IReadOnlyList<string> _closeMissionResultOptions => EinsatzAbschlussService.ErgebnisOptionen;

    private List<PersonalEntry> _personalList = new();
    private List<DogEntry> _dogList = new();
    private List<DroneEntry> _droneList = new();
    private readonly HashSet<string> _selectedPersonnelIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedDogIds = new(StringComparer.OrdinalIgnoreCase);

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
    private string? _tabletActivePanelId;
    private bool _summaryGridCollapsed;
    private readonly HashSet<string> _expandedTeamIds = new();
    private TeamStatusFilter _teamStatusFilter = TeamStatusFilter.All;
    private bool _showTeamFilter;
    private DotNetObjectReference<EinsatzMonitor>? _teamOrderingReference;
    private bool _teamOrderingInitialized;

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
        EinsatzService.TeamsReordered += OnTeamsReordered;
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
        UpdateMissionTopbar();
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
        var teamListIsRendered = HasActiveEinsatz
            && EinsatzService.Teams.Count > 0
            && _currentLayout.Any(panel => panel.PanelId == KnownPanels.Teams && panel.IsVisible);

        if (teamListIsRendered && !_teamOrderingInitialized)
        {
            _teamOrderingReference ??= DotNetObjectReference.Create(this);
            _teamOrderingInitialized = await JS.InvokeAsync<bool>(
                "teamOrdering.initialize",
                "teams-monitor-list",
                _teamOrderingReference);
        }
        else if (!teamListIsRendered)
        {
            if (_teamOrderingInitialized)
            {
                try
                {
                    await JS.InvokeVoidAsync("teamOrdering.dispose", "teams-monitor-list");
                }
                catch (JSDisconnectedException)
                {
                }
            }
            _teamOrderingInitialized = false;
        }

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

        if (!string.IsNullOrWhiteSpace(_pendingVermisstenModalSectionScrollTarget))
        {
            await JS.InvokeVoidAsync("layoutTools.scrollToElement", _pendingVermisstenModalSectionScrollTarget);
            _pendingVermisstenModalSectionScrollTarget = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        EinsatzService.TeamAdded -= OnTeamChanged;
        EinsatzService.TeamRemoved -= OnTeamChanged;
        EinsatzService.TeamUpdated -= OnTeamChanged;
        EinsatzService.TeamsReordered -= OnTeamsReordered;
        EinsatzService.EinsatzChanged -= OnEinsatzChanged;
        EinsatzService.NoteAdded -= OnNoteAdded;
        CollarTrackingService.CollarLocationReceived -= OnCollarLocationChanged;
        CollarTrackingService.CollarHistoryCleared -= OnCollarHistoryChanged;
        MissionTopbar.ClearContent(this);
        _weatherRefreshTimer?.Dispose();
        _durationRefreshTimer?.Dispose();
        _screensaverClockTimer?.Dispose();

        if (_teamOrderingInitialized)
        {
            try
            {
                await JS.InvokeVoidAsync("teamOrdering.dispose", "teams-monitor-list");
            }
            catch (JSDisconnectedException)
            {
            }
        }

        _teamOrderingReference?.Dispose();
    }

    // ===== Dashboard-Layout-Methoden =====

}

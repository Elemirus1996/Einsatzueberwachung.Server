using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Services;
using Einsatzueberwachung.Server.Training;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Layout;

public partial class MainLayout : LayoutComponentBase, IAsyncDisposable
{
    [Inject] private BrowserPreferencesService BrowserPrefs { get; set; } = default!;
    [Inject] private IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] private TrainerNotificationService TrainerNotifications { get; set; } = default!;
    [Inject] private IWarningService WarningService { get; set; } = default!;
    [Inject] private ITimeService TimeService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private bool _isDarkMode;
    private bool _sidebarCollapsed;
    private bool _hilfeOpen;
    private DotNetObjectReference<MainLayout>? _dotNetRef;
    private bool _showCriticalWarningPopup;
    private string _criticalWarningTitle = string.Empty;
    private string _criticalWarningMessage = string.Empty;
    private bool _audioEnableHintVisible;

    private bool _showExerciseEndedPopup;
    private string _exerciseEndedName = string.Empty;
    private string _exerciseEndedSummary = string.Empty;
    private bool _szenarioMenuOpen;
    private TimeSpan? _missionDuration;
    private System.Threading.Timer? _missionDurationTimer;

    private WarningEntry? _lastWarning;

    private bool HasActiveEinsatz =>
        !string.IsNullOrWhiteSpace(EinsatzService.CurrentEinsatz.Einsatzort)
        && EinsatzService.CurrentEinsatz.EinsatzEnde is null;

    private int RunningTeamsCount => EinsatzService.Teams.Count(team => team.IsRunning);

    private int PausingTeamsCount => EinsatzService.Teams.Count(team => team.IsPausing);

    private int ReadyTeamsCount => EinsatzService.Teams.Count(team => !team.IsDroneTeam && !team.IsSupportTeam && !team.IsRunning && !team.IsPausing);

    protected override void OnInitialized()
    {
        Navigation.LocationChanged += OnLocationChanged;
        EinsatzService.EinsatzChanged += OnEinsatzStateChanged;
        EinsatzService.TeamAdded += OnTeamStateChanged;
        EinsatzService.TeamUpdated += OnTeamStateChanged;
        EinsatzService.TeamRemoved += OnTeamStateChanged;
        EinsatzService.TeamWarningTriggered += OnTeamWarningTriggered;
        EinsatzService.DogPauseStarted += OnDogPauseStarted;
        TrainerNotifications.ExerciseEnded += OnExerciseEnded;
        WarningService.WarningAdded += OnWarningAdded;
    }

    private void OnWarningAdded(WarningEntry warning)
    {
        _ = InvokeAsync(() =>
        {
            _lastWarning = warning;
            StateHasChanged();
        });
    }

    private string LastWarningBadgeClass => _lastWarning?.Level switch
    {
        WarningLevel.Critical => "text-bg-danger",
        WarningLevel.Info     => "text-bg-info",
        _                     => "text-bg-warning"
    };

    private void OnExerciseEnded(string exerciseName, string summary)
    {
        _ = InvokeAsync(() =>
        {
            _exerciseEndedName = exerciseName;
            _exerciseEndedSummary = summary;
            _showExerciseEndedPopup = true;
            StateHasChanged();
        });
    }

    private void DismissExerciseEndedPopup()
    {
        _showExerciseEndedPopup = false;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await BrowserPrefs.LoadAsync();
        _isDarkMode = BrowserPrefs.Preferences.IsDarkMode;

        var sc = BrowserPrefs.Preferences.Shortcuts;
        await JS.InvokeVoidAsync("keyboardShortcuts.configure", new {
            navHome    = sc.NavHome,
            navKarte   = sc.NavKarte,
            navMonitor = sc.NavMonitor,
            navStart   = sc.NavStart
        });

        _sidebarCollapsed = await JS.InvokeAsync<bool>("layoutTools.getSidebarCollapsed");
        _dotNetRef = DotNetObjectReference.Create(this);

        await JS.InvokeVoidAsync(
            "themeSync.init",
            _dotNetRef,
            _isDarkMode,
            BrowserPrefs.Preferences.ThemePreset,
            BrowserPrefs.Preferences.VisualIntensity);

        if (BrowserPrefs.Preferences.ThemeMode == "Auto")
        {
            await JS.InvokeVoidAsync("themeSync.watchSystemTheme");
        }
        else
        {
            await ApplyPersistedThemeAsync();
        }

        await JS.InvokeVoidAsync("initializeClock");
        RefreshMissionDuration();
        StartMissionDurationTimer();
        StateHasChanged();
    }

    private void OnEinsatzStateChanged()
    {
        _szenarioMenuOpen = false;
        RefreshMissionDuration();
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnTeamStateChanged(Team team)
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnTeamWarningTriggered(Team team, bool isSecondWarning)
    {
        _ = InvokeAsync(async () =>
        {
            var prefs = BrowserPrefs.Preferences;
            var audioStarted = true;
            if (prefs.SoundAlertsEnabled)
            {
                var soundType = isSecondWarning ? prefs.SecondWarningSound : prefs.FirstWarningSound;
                var frequency = isSecondWarning ? prefs.SecondWarningFrequency : prefs.FirstWarningFrequency;
                var shouldRepeat = isSecondWarning && prefs.RepeatSecondWarning;
                var repeatSeconds = Math.Max(1, prefs.RepeatWarningIntervalSeconds);

                try
                {
                    audioStarted = await JS.InvokeAsync<bool>(
                        "layoutTools.playWarningAlert",
                        soundType,
                        frequency,
                        prefs.SoundVolume,
                        shouldRepeat,
                        repeatSeconds);
                }
                catch
                {
                    audioStarted = false;
                }
            }

            _audioEnableHintVisible = prefs.SoundAlertsEnabled && !audioStarted;

            if (isSecondWarning)
            {
                _criticalWarningTitle = $"{team.TeamName} hat kritische Warnstufe erreicht";
                _criticalWarningMessage = $"Timer: {team.ElapsedTime:hh\\:mm\\:ss} - Bitte Teamstatus pruefen.";
                _showCriticalWarningPopup = true;
            }

            WarningService.AddWarning(new WarningEntry
            {
                Title   = isSecondWarning ? "Kritische Warnstufe erreicht" : "Erste Warnstufe erreicht",
                Message = isSecondWarning
                    ? $"{team.TeamName} - Timer: {team.ElapsedTime:hh\\:mm\\:ss} - Bitte Teamstatus pruefen."
                    : $"{team.TeamName} - Timer: {team.ElapsedTime:hh\\:mm\\:ss}",
                Level          = isSecondWarning ? WarningLevel.Critical : WarningLevel.Warning,
                TeamId         = team.TeamId,
                NavigationUrl  = "/einsatz-monitor",
                Source         = isSecondWarning ? WarningRuleDefinition.Sources.TeamTimerCritical : WarningRuleDefinition.Sources.TeamTimer,
                Timestamp      = TimeService.Now
            });

            StateHasChanged();
        });
    }

    private void OnDogPauseStarted(Team team)
    {
        WarningService.AddWarning(new WarningEntry
        {
            Title = "Hund braucht Pause",
            Message = $"{team.DogName} (Team: {team.TeamName}) - {team.RequiredPauseMinutes} Min. Pause erforderlich.",
            Level = WarningLevel.Warning,
            TeamId = team.TeamId,
            NavigationUrl = $"/einsatz-monitor#team-{team.TeamId}",
            Source = WarningRuleDefinition.Sources.DogPause,
            Timestamp = TimeService.Now
        });
    }

    private async Task SetSzenarioAsync(EinsatzSzenarioType szenario)
    {
        _szenarioMenuOpen = false;
        await EinsatzService.UpdateSzenarioAsync(szenario);
    }

    private void StartMissionDurationTimer()
    {
        _missionDurationTimer?.Dispose();
        _missionDurationTimer = new System.Threading.Timer(
            _ => _ = InvokeAsync(() =>
            {
                RefreshMissionDuration();
                StateHasChanged();
            }),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    private void RefreshMissionDuration()
    {
        if (!HasActiveEinsatz || !EinsatzService.CurrentEinsatz.AlarmierungsZeit.HasValue)
        {
            _missionDuration = null;
            return;
        }

        var duration = TimeService.Now - EinsatzService.CurrentEinsatz.AlarmierungsZeit.Value;
        _missionDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    private string GetMissionDurationDisplay()
    {
        return (_missionDuration ?? EinsatzService.CurrentEinsatz.Dauer)?.ToString(@"hh\:mm\:ss") ?? "--:--:--";
    }

    private async Task DismissCriticalWarningAsync()
    {
        _showCriticalWarningPopup = false;
        _audioEnableHintVisible = false;

        try
        {
            await JS.InvokeVoidAsync("layoutTools.stopWarningAlert");
        }
        catch
        {
            // Ignore JS disconnects while closing popup.
        }
    }

    private async Task ApplyPersistedThemeAsync()
    {
        _isDarkMode = BrowserPrefs.Preferences.IsDarkMode;
        await JS.InvokeVoidAsync("themeSync.setThemeState", new
        {
            isDark = _isDarkMode,
            preset = BrowserPrefs.Preferences.ThemePreset,
            intensity = BrowserPrefs.Preferences.VisualIntensity
        });
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        try
        {
            await ApplyPersistedThemeAsync();
            await InvokeAsync(StateHasChanged);
        }
        catch (JSDisconnectedException)
        {
            // Ignore navigation-time JS disconnects.
        }
        catch (TaskCanceledException)
        {
            // Ignore cancellation during shutdown/disconnect.
        }
        catch (Exception)
        {
            // Prevent unhandled exception crash in async void event handler.
        }
    }

    private async Task ToggleSidebarAsync()
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        await JS.InvokeVoidAsync("layoutTools.setSidebarCollapsed", _sidebarCollapsed);
    }

    private void GoHome()
    {
        Navigation.NavigateTo("/");
    }

    private void OpenCloseMissionFromTopbar()
    {
        var token = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Navigation.NavigateTo($"/einsatz-monitor?openCloseMissionAt={token}");
    }

    private async Task ToggleThemeAsync()
    {
        if (BrowserPrefs.Preferences.ThemeMode != "Manual")
            return;

        _isDarkMode = !_isDarkMode;
        BrowserPrefs.Update(p => p.IsDarkMode = _isDarkMode);
        await BrowserPrefs.SaveAsync();
        await BrowserPrefs.SaveThemeToServerAsync();
        await JS.InvokeVoidAsync("themeSync.setThemeState", new
        {
            isDark = _isDarkMode,
            preset = BrowserPrefs.Preferences.ThemePreset,
            intensity = BrowserPrefs.Preferences.VisualIntensity
        });
    }

    private async Task ToggleFullscreenAsync()
    {
        await JS.InvokeVoidAsync("layoutTools.toggleFullscreen");
    }

    [JSInvokable]
    public async Task OnThemeChangedFromStorage(bool isDark)
    {
        _isDarkMode = isDark;
        BrowserPrefs.Update(p => p.IsDarkMode = isDark);
        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        Navigation.LocationChanged -= OnLocationChanged;
        EinsatzService.EinsatzChanged -= OnEinsatzStateChanged;
        EinsatzService.TeamAdded -= OnTeamStateChanged;
        EinsatzService.TeamUpdated -= OnTeamStateChanged;
        EinsatzService.TeamRemoved -= OnTeamStateChanged;
        EinsatzService.TeamWarningTriggered -= OnTeamWarningTriggered;
        EinsatzService.DogPauseStarted -= OnDogPauseStarted;
        TrainerNotifications.ExerciseEnded -= OnExerciseEnded;
        WarningService.WarningAdded -= OnWarningAdded;
        _missionDurationTimer?.Dispose();

        try
        {
            await JS.InvokeVoidAsync("themeSync.stopWatchingSystemTheme");
            await JS.InvokeVoidAsync("themeSync.dispose");
            await JS.InvokeVoidAsync("layoutTools.stopWarningAlert");
        }
        catch
        {
            // Ignore disposal errors during disconnect/shutdown.
        }

        _dotNetRef?.Dispose();
    }
}

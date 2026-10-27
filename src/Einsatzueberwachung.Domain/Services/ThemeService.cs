// Einsatzüberwachung - Globaler Theme Service
// Stellt sicher, dass Theme über alle Seiten/Komponenten synchron ist

using Einsatzueberwachung.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public class ThemeService
    {
        private readonly ISettingsService _settingsService;
        private readonly ITimeService? _timeService;
        private readonly ILogger<ThemeService> _logger;
        private bool _isDarkMode;
        private bool _isInitialized = false;
        private System.Threading.Timer? _scheduleTimer;

        public event Action? OnThemeChanged;

        public ThemeService(ISettingsService settingsService, ILogger<ThemeService> logger, ITimeService? timeService = null)
        {
            _settingsService = settingsService;
            _timeService = timeService;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            if (!_isInitialized)
            {
                await LoadThemeAsync();
                await CheckScheduledTheme();
                StartScheduleTimer();
                _isInitialized = true;
            }
        }

        public bool IsDarkMode => _isDarkMode;

        public string CurrentTheme => _isDarkMode ? "dark" : "light";

        public async Task LoadThemeAsync()
        {
            try
            {
                _isDarkMode = await _settingsService.GetIsDarkModeAsync();
                _logger.LogInformation("Theme geladen: {Theme}", CurrentTheme);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Laden des Themes");
                _isDarkMode = false;
            }
        }

        public async Task SetThemeAsync(bool isDark)
        {
            if (_isDarkMode != isDark)
            {
                _isDarkMode = isDark;

                try
                {
                    await _settingsService.SetIsDarkModeAsync(isDark);
                    _logger.LogInformation("Theme gespeichert: {Theme}", CurrentTheme);
                    OnThemeChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler beim Speichern des Themes");
                }
            }
        }

        public async Task ToggleThemeAsync()
        {
            await SetThemeAsync(!_isDarkMode);
        }

        private async Task CheckScheduledTheme()
        {
            try
            {
                var appSettings = await _settingsService.GetAppSettingsAsync();
                if (appSettings?.ThemeMode == "Scheduled")
                {
                    var now = (_timeService?.Now ?? Ortszeit.Jetzt).TimeOfDay;
                    var start = appSettings.DarkModeStartTime;
                    var end = appSettings.DarkModeEndTime;

                    bool shouldBeDark;
                    if (start < end)
                    {
                        // Normal case: z.B. 09:00 - 18:00
                        shouldBeDark = now >= start && now < end;
                    }
                    else
                    {
                        // Overnight case: z.B. 20:00 - 06:00
                        shouldBeDark = now >= start || now < end;
                    }

                    if (shouldBeDark != _isDarkMode)
                    {
                        await SetThemeAsync(shouldBeDark);
                        _logger.LogInformation("Zeitgesteuerter Theme-Wechsel zu: {Theme}", CurrentTheme);
                    }
                }
                // "Auto" wird client-seitig via prefers-color-scheme behandelt
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler bei zeitgesteuertem Theme-Wechsel");
            }
        }

        private void StartScheduleTimer()
        {
            // Prüfe jede Minute ob Theme gewechselt werden muss (nur für "Scheduled"-Modus)
            _scheduleTimer = new System.Threading.Timer(
                _ => {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await CheckScheduledTheme();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Fehler im Schedule-Timer");
                        }
                    });
                },
                null,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMinutes(1)
            );

            _logger.LogInformation("Schedule-Timer gestartet - prüft alle 60 Sekunden");
        }

        public void Dispose()
        {
            _scheduleTimer?.Dispose();
        }
    }
}

// Divera 24/7 API Service
// Laedt Alarme und Verfuegbarkeitsstatus ueber die Divera REST API
// API-Key wird aus ISettingsService gelesen — NICHT aus appsettings.json

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models.Divera;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class DiveraService : IDiveraService
    {
        private readonly HttpClient _httpClient;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<DiveraService> _logger;

        private string _accessKey = string.Empty;
        private string _baseUrl = "https://app.divera247.com/api/v2";
        private bool _enabled;
        private bool _configLoaded;
        private TimeZoneInfo _appTimeZone = TimeZoneInfo.Local;

        private DiveraPullResponse? _cachedPull;
        private DateTime _cacheTime = DateTime.MinValue;

        private DiveraAlarm? _cachedLastAlarm;
        private DateTime _lastAlarmCacheTime = DateTime.MinValue;

        private int _pollIntervalIdleSeconds = 600;
        private int _pollIntervalActiveSeconds = 60;

        private TimeSpan PullCacheDuration =>
            (_cachedPull?.Alarms.Any(a => !a.Closed) == true) ||
            (_cachedLastAlarm != null && !_cachedLastAlarm.Closed && _cachedLastAlarm.Id > 0)
                ? TimeSpan.FromSeconds(_pollIntervalActiveSeconds)
                : TimeSpan.FromSeconds(_pollIntervalIdleSeconds);

        private TimeSpan LastAlarmCacheDuration =>
            (_cachedLastAlarm != null && !_cachedLastAlarm.Closed && _cachedLastAlarm.Id > 0)
                ? TimeSpan.FromSeconds(_pollIntervalActiveSeconds)
                : TimeSpan.FromSeconds(_pollIntervalIdleSeconds);

        public int PollIntervalIdleSeconds => _pollIntervalIdleSeconds;
        public int PollIntervalActiveSeconds => _pollIntervalActiveSeconds;

        public bool HasActiveAlarms =>
            (_cachedPull?.Alarms.Any(a => !a.Closed) == true) ||
            (_cachedLastAlarm != null && !_cachedLastAlarm.Closed && _cachedLastAlarm.Id > 0);

        public event Action? DataChanged;

        public bool IsConfigured => _enabled && !string.IsNullOrWhiteSpace(_accessKey);

        public DiveraService(HttpClient httpClient, ISettingsService settingsService, ILogger<DiveraService> logger)
        {
            _httpClient = httpClient;
            _settingsService = settingsService;
            _logger = logger;
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Einsatzueberwachung.Server/1.0");
        }

        public async Task RefreshConfigurationAsync()
        {
            _configLoaded = false;
            _cachedPull = null;
            _cacheTime = DateTime.MinValue;
            _cachedLastAlarm = null;
            _lastAlarmCacheTime = DateTime.MinValue;
            await LoadConfigAsync();
        }

        private async Task LoadConfigIfNeededAsync()
        {
            if (!_configLoaded)
                await LoadConfigAsync();
        }

        private async Task LoadConfigAsync()
        {
            var settings = await _settingsService.GetAppSettingsAsync();
            _accessKey = settings.DiveraAccessKey ?? string.Empty;
            _baseUrl = string.IsNullOrWhiteSpace(settings.DiveraBaseUrl)
                ? "https://app.divera247.com/api/v2"
                : settings.DiveraBaseUrl.TrimEnd('/');
            _enabled = settings.DiveraEnabled;
            _appTimeZone = FindTimeZone(settings.TimeZoneId);
            _pollIntervalIdleSeconds = settings.DiveraPollIntervalIdleSeconds > 0
                ? settings.DiveraPollIntervalIdleSeconds : 600;
            _pollIntervalActiveSeconds = settings.DiveraPollIntervalActiveSeconds > 0
                ? settings.DiveraPollIntervalActiveSeconds : 60;
            _configLoaded = true;
            _logger.LogDebug("Divera-Konfiguration geladen. Enabled={Enabled}, IdleInterval={Idle}s, ActiveInterval={Active}s",
                _enabled, _pollIntervalIdleSeconds, _pollIntervalActiveSeconds);
        }

        private string GetApiHostUrl()
        {
            var url = _baseUrl.TrimEnd('/');
            foreach (var suffix in new[] { "/api/v2", "/api" })
            {
                if (url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return url[..^suffix.Length];
            }
            return url;
        }

        private DateTime ConvertUnixToAppTime(long unixSeconds)
        {
            var utc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
            return TimeZoneInfo.ConvertTimeFromUtc(utc, _appTimeZone);
        }

        private static TimeZoneInfo FindTimeZone(string? tzId)
        {
            if (string.IsNullOrWhiteSpace(tzId))
                return TimeZoneInfo.Local;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(tzId);
            }
            catch (TimeZoneNotFoundException)
            {
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(tzId, out var windowsId))
                {
                    try { return TimeZoneInfo.FindSystemTimeZoneById(windowsId); }
                    catch { /* ignore */ }
                }
                return TimeZoneInfo.Local;
            }
        }
    }
}

using Einsatzueberwachung.Domain.Interfaces;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Liefert die aktuelle Uhrzeit in der konfigurierten IANA-Zeitzone.
    /// Singleton – nach Änderung der Zeitzone in den Einstellungen Refresh() aufrufen.
    /// </summary>
    public class AppTimeService : ITimeService
    {
        private readonly ISettingsService _settingsService;
        private TimeZoneInfo? _timeZone;
        private readonly object _lock = new();

        public AppTimeService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public DateTime Now
        {
            get
            {
                var tz = EnsureTimeZone();
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
        }

        public void Refresh()
        {
            lock (_lock)
            {
                _timeZone = LoadTimeZone();
                Ortszeit.Zone = _timeZone;
            }
        }

        private TimeZoneInfo EnsureTimeZone()
        {
            if (_timeZone is not null) return _timeZone;
            lock (_lock)
            {
                if (_timeZone is null)
                {
                    _timeZone = LoadTimeZone();
                    Ortszeit.Zone = _timeZone;
                }
                return _timeZone;
            }
        }

        private TimeZoneInfo LoadTimeZone()
        {
            try
            {
                var settings = _settingsService.GetAppSettingsAsync().GetAwaiter().GetResult();
                return FindTimeZone(settings.TimeZoneId);
            }
            catch
            {
                return TimeZoneInfo.Local;
            }
        }

        private static TimeZoneInfo FindTimeZone(string? tzId)
        {
            if (string.IsNullOrWhiteSpace(tzId))
                return TimeZoneInfo.Local;

            // .NET 6+ unterstützt IANA-IDs auf allen Plattformen
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(tzId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Fallback: versuche Windows-ID (Entwicklungsrechner o.Ä.)
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

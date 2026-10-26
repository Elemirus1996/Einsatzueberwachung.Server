// Divera 24/7 API Service
// Laedt Alarme und Verfuegbarkeitsstatus ueber die Divera REST API
// API-Key wird aus ISettingsService gelesen — NICHT aus appsettings.json
//
// Eine Instanz fuer die ganze Anwendung (Singleton): alle Seiten teilen sich den Zwischenspeicher, damit mehrere
// offene Tablets/Monitore nicht je eigene Anfragen an Divera schicken. Bei Fehlern und HTTP 429 wird mit
// wachsendem Abstand erneut gefragt (Divera warnt ausdruecklich vor Rate-Limits).

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models.Divera;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class DiveraService : IDiveraService
    {
        private readonly HttpClient _httpClient;
        private readonly ISettingsService _settingsService;
        private readonly IMasterDataService _masterDataService;
        private readonly ILogger<DiveraService> _logger;
        private readonly SemaphoreSlim _fetchLock = new(1, 1);

        private string _accessKey = string.Empty;
        private string _baseUrl = "https://app.divera247.com/api/v2";
        private bool _enabled;
        private bool _configLoaded;
        private TimeZoneInfo _appTimeZone = TimeZoneInfo.Local;
        private Dictionary<int, DiveraRueckmeldung> _statusZuordnung = new();
        private bool _alarmeUebernehmen = true;
        private bool _statusAnzeigen = true;
        private bool _fotosSenden;

        private DiveraPullResponse? _cachedPull;
        private DateTime _cacheTime = DateTime.MinValue;

        private DiveraAlarm? _cachedLastAlarm;
        private DateTime _lastAlarmCacheTime = DateTime.MinValue;

        private int _pollIntervalIdleSeconds = 600;
        private int _pollIntervalActiveSeconds = 60;

        // Backoff nach Fehlern (gemeinsam fuer alle Divera-Abfragen)
        private int _failureCount;
        private DateTime _backoffUntilUtc = DateTime.MinValue;

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

        public bool AlarmeUebernehmen => IsConfigured && _alarmeUebernehmen;
        public bool StatusAnzeigen => IsConfigured && _statusAnzeigen;
        public bool FotosSenden => IsConfigured && _fotosSenden;

        // Zuletzt geladene Status der Einheit – bleiben beim Speichern der Einstellungen erhalten (Zuordnungs-Tabelle)
        private IReadOnlyList<DiveraStatusDefinition> _statusDefinitionen = new List<DiveraStatusDefinition>();
        public IReadOnlyList<DiveraStatusDefinition> StatusDefinitionen => _statusDefinitionen;

        /// <param name="httpClient">Langlebiger Client; ohne Angabe einer mit regelmaessig erneuerten Verbindungen (DNS).</param>
        public DiveraService(
            ISettingsService settingsService,
            IMasterDataService masterDataService,
            ILogger<DiveraService> logger,
            HttpClient? httpClient = null)
        {
            _settingsService = settingsService;
            _masterDataService = masterDataService;
            _logger = logger;
            _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Einsatzueberwachung.Server/1.0");
        }

        public async Task RefreshConfigurationAsync()
        {
            _configLoaded = false;
            _cachedPull = null;
            _cacheTime = DateTime.MinValue;
            _cachedLastAlarm = null;
            _lastAlarmCacheTime = DateTime.MinValue;
            _failureCount = 0;
            _backoffUntilUtc = DateTime.MinValue;
            await LoadConfigAsync();
            DataChanged?.Invoke();
        }

        public Task LadeEinstellungenAsync() => LoadConfigIfNeededAsync();

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
            _statusZuordnung = settings.DiveraStatusZuordnung != null
                ? new Dictionary<int, DiveraRueckmeldung>(settings.DiveraStatusZuordnung)
                : new Dictionary<int, DiveraRueckmeldung>();
            _alarmeUebernehmen = settings.DiveraAlarmeUebernehmen;
            _statusAnzeigen = settings.DiveraStatusAnzeigen;
            _fotosSenden = settings.DiveraFotosSenden;
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

        // ── Backoff ──────────────────────────────────────────────────

        private bool InBackoff => DateTime.UtcNow < _backoffUntilUtc;

        private void RecordSuccess()
        {
            _failureCount = 0;
            _backoffUntilUtc = DateTime.MinValue;
        }

        private void RecordFailure(HttpResponseMessage? response)
        {
            _failureCount++;
            var wait = BackoffDelay(_failureCount, response?.StatusCode == System.Net.HttpStatusCode.TooManyRequests,
                response?.Headers.RetryAfter?.Delta);
            _backoffUntilUtc = DateTime.UtcNow + wait;
            _logger.LogWarning("Divera: naechster Versuch fruehestens in {Seconds}s (Fehler Nr. {Count})",
                (int)wait.TotalSeconds, _failureCount);
        }

        /// <summary>15 s, 30 s, 60 s … hoechstens 5 min; bei 429 mindestens 60 s oder so lange wie Divera vorgibt.</summary>
        public static TimeSpan BackoffDelay(int failureCount, bool tooManyRequests, TimeSpan? retryAfter)
        {
            var exponent = Math.Min(Math.Max(failureCount, 1) - 1, 5);
            var delay = TimeSpan.FromSeconds(Math.Min(15 * Math.Pow(2, exponent), 300));
            if (tooManyRequests)
            {
                var minimum = retryAfter ?? TimeSpan.FromSeconds(60);
                if (delay < minimum) delay = minimum;
            }
            return delay;
        }

        // ── Anreichern: Status-Namen/-Bedeutung und Namen aus den Stammdaten ─────────────────

        private async Task AnreichernAsync(DiveraPullResponse? pull, IEnumerable<DiveraAlarm> alarms)
        {
            Dictionary<int, string> stammdaten;
            try
            {
                stammdaten = (await _masterDataService.GetPersonalListAsync())
                    .Where(p => p.DiveraUserId.HasValue && !string.IsNullOrWhiteSpace(p.FullName))
                    .GroupBy(p => p.DiveraUserId!.Value)
                    .ToDictionary(g => g.Key, g => g.First().FullName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Divera: Stammdaten fuer Namensaufloesung nicht lesbar");
                stammdaten = new Dictionary<int, string>();
            }

            Anreichern(pull, alarms, stammdaten, _statusZuordnung);
        }

        /// <summary>
        /// Namen: zuerst aus den Stammdaten (Feld „Divera Benutzer-ID“), sonst aus Divera, sonst „#ID“.
        /// Status: Name/Farbe aus den Definitionen der Einheit; Bedeutung aus der Zuordnung in den Einstellungen,
        /// sonst aus der Farbe, sonst (alte Schnittstelle) 1/2/3.
        /// </summary>
        public static void Anreichern(
            DiveraPullResponse? pull,
            IEnumerable<DiveraAlarm> alarms,
            IReadOnlyDictionary<int, string> stammdatenNamen,
            IReadOnlyDictionary<int, DiveraRueckmeldung> zuordnung)
        {
            var definitionen = pull?.StatusDefinitions ?? new Dictionary<int, DiveraStatusDefinition>();
            var diveraNamen = pull?.Members
                .Where(m => !string.IsNullOrWhiteSpace(m.FullName))
                .GroupBy(m => m.Id)
                .ToDictionary(g => g.Key, g => g.First().FullName) ?? new Dictionary<int, string>();

            if (pull != null)
            {
                foreach (var member in pull.Members)
                {
                    if (stammdatenNamen.TryGetValue(member.Id, out var name))
                        member.StammdatenName = name;
                }
            }

            foreach (var alarm in alarms)
            {
                foreach (var entry in alarm.UcrDetails)
                {
                    entry.MemberName = stammdatenNamen.TryGetValue(entry.MemberId, out var stamm) ? stamm
                        : diveraNamen.TryGetValue(entry.MemberId, out var divera) ? divera
                        : $"#{entry.MemberId}";

                    if (entry.Status == 0)
                    {
                        entry.Bedeutung = DiveraRueckmeldung.Unbekannt;
                        continue;
                    }

                    if (definitionen.TryGetValue(entry.Status, out var def))
                    {
                        entry.StatusName = def.Name;
                        entry.StatusColor = def.Color;
                    }

                    entry.Bedeutung = BedeutungFuer(entry.Status, entry.StatusColor, zuordnung);
                }
            }
        }

        public static DiveraRueckmeldung BedeutungFuer(int statusId, string? farbe, IReadOnlyDictionary<int, DiveraRueckmeldung> zuordnung)
        {
            if (zuordnung.TryGetValue(statusId, out var vorgabe)) return vorgabe;
            var ausFarbe = DiveraStatusFarbe.Bedeutung(farbe);
            if (ausFarbe != DiveraRueckmeldung.Unbekannt) return ausFarbe;
            return statusId switch
            {
                1 => DiveraRueckmeldung.Kommt,
                2 => DiveraRueckmeldung.KommtNicht,
                3 => DiveraRueckmeldung.KommtSpaeter,
                _ => DiveraRueckmeldung.Unbekannt
            };
        }
    }
}

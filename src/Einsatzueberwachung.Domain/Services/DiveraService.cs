// Divera 24/7 API Service
// Laedt Alarme und Verfuegbarkeitsstatus ueber die Divera REST API
// API-Key wird aus ISettingsService gelesen — NICHT aus appsettings.json

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models.Divera;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public class DiveraService : IDiveraService
    {
        private readonly HttpClient _httpClient;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<DiveraService> _logger;

        private string _accessKey = string.Empty;
        private string _baseUrl = "https://app.divera247.com/api/v2";
        private bool _enabled;
        private bool _configLoaded;

        // Cache fuer PullAll-Antwort — Dauer haengt davon ab ob Alarme aktiv sind
        private DiveraPullResponse? _cachedPull;
        private DateTime _cacheTime = DateTime.MinValue;

        // Konfigurierbare Poll-Intervalle (in Sekunden)
        private int _pollIntervalIdleSeconds = 600;   // 10 Minuten bei Ruhe
        private int _pollIntervalActiveSeconds = 60;  // 1 Minute bei aktivem Alarm

        /// <summary>Aktuell gueltige Cache-Dauer abhaengig vom Alarm-Status</summary>
        private TimeSpan CurrentCacheDuration =>
            _cachedPull?.Alarms.Any(a => !a.Closed) == true
                ? TimeSpan.FromSeconds(_pollIntervalActiveSeconds)
                : TimeSpan.FromSeconds(_pollIntervalIdleSeconds);

        /// <summary>Poll-Intervall wenn kein Alarm aktiv (Sekunden)</summary>
        public int PollIntervalIdleSeconds => _pollIntervalIdleSeconds;

        /// <summary>Poll-Intervall wenn Alarm aktiv (Sekunden)</summary>
        public int PollIntervalActiveSeconds => _pollIntervalActiveSeconds;

        /// <summary>Gibt an ob aktuell mindestens ein offener Alarm vorliegt</summary>
        public bool HasActiveAlarms => _cachedPull?.Alarms.Any(a => !a.Closed) == true;

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
            // Cache invalidieren bei Konfig-Aenderung
            _cachedPull = null;
            _cacheTime = DateTime.MinValue;
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
            _pollIntervalIdleSeconds = settings.DiveraPollIntervalIdleSeconds > 0
                ? settings.DiveraPollIntervalIdleSeconds : 600;
            _pollIntervalActiveSeconds = settings.DiveraPollIntervalActiveSeconds > 0
                ? settings.DiveraPollIntervalActiveSeconds : 60;
            _configLoaded = true;
            _logger.LogDebug("Divera-Konfiguration geladen. Enabled={Enabled}, IdleInterval={Idle}s, ActiveInterval={Active}s",
                _enabled, _pollIntervalIdleSeconds, _pollIntervalActiveSeconds);
        }

        /// <summary>
        /// Hauptmethode: Ruft pull/all ab und liefert gecachte Ergebnisse.
        /// Alle anderen Methoden delegieren hierher, damit nur ein API-Call noetig ist.
        /// </summary>
        public async Task<DiveraPullResponse?> PullAllAsync()
        {
            await LoadConfigIfNeededAsync();

            if (!IsConfigured)
            {
                _logger.LogDebug("Divera: Nicht konfiguriert — PullAll wird uebersprungen.");
                return null;
            }

            // Cache pruefen — Dauer ist dynamisch je nach Alarm-Status
            if (_cachedPull != null && DateTime.UtcNow - _cacheTime < CurrentCacheDuration)
            {
                return _cachedPull;
            }

            try
            {
                var url = $"{_baseUrl}/pull/all?accesskey={_accessKey}";
                _logger.LogInformation("Divera PullAll: GET {Url}", _baseUrl + "/pull/all");

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Divera PullAll fehlgeschlagen: HTTP {StatusCode}", response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = ParsePullAllResponse(json);

                if (result != null)
                {
                    _cachedPull = result;
                    _cacheTime = DateTime.UtcNow;
                    DataChanged?.Invoke();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Abrufen von Divera PullAll");
                return null;
            }
        }

        public async Task<List<DiveraAlarm>> GetActiveAlarmsAsync()
        {
            var pull = await PullAllAsync();
            if (pull == null)
                return new List<DiveraAlarm>();

            return pull.Alarms.Where(a => !a.Closed).ToList();
        }

        public async Task<DiveraAlarm?> GetAlarmByIdAsync(int alarmId)
        {
            var pull = await PullAllAsync();
            return pull?.Alarms.FirstOrDefault(a => a.Id == alarmId);
        }

        public async Task<List<DiveraMember>> GetMembersWithStatusAsync()
        {
            var pull = await PullAllAsync();
            if (pull == null)
                return new List<DiveraMember>();

            // Nach Verfuegbarkeit sortieren: Verfuegbar zuerst
            return pull.Members
                .OrderBy(m => m.StatusId == 0 ? 99 : m.StatusId)
                .ThenBy(m => m.Lastname)
                .ThenBy(m => m.Firstname)
                .ToList();
        }

        public async Task<List<DiveraMember>> GetAvailableMembersAsync()
        {
            var members = await GetMembersWithStatusAsync();
            return members.Where(m => m.IsVerfuegbar).ToList();
        }

        public async Task<bool> TestConnectionAsync()
        {
            await LoadConfigIfNeededAsync();

            if (!IsConfigured)
                return false;

            try
            {
                var url = $"{_baseUrl}/pull/all?accesskey={_accessKey}";
                var response = await _httpClient.GetAsync(url);
                _logger.LogInformation("Divera Verbindungstest: HTTP {StatusCode}", response.StatusCode);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Divera Verbindungstest fehlgeschlagen");
                return false;
            }
        }

        /// <summary>
        /// Parst die JSON-Antwort von pull/all.
        /// Divera liefert Objects mit numerischen Keys — keine Arrays.
        /// </summary>
        private DiveraPullResponse? ParsePullAllResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
                {
                    _logger.LogWarning("Divera API: success=false in der Antwort");
                    return null;
                }

                if (!root.TryGetProperty("data", out var dataElement))
                {
                    _logger.LogWarning("Divera API: Kein 'data'-Feld in der Antwort");
                    return null;
                }

                var result = new DiveraPullResponse
                {
                    LastUpdated = DateTime.Now
                };

                // --- Status-Definitionen aus cluster.status_sorter ---
                if (dataElement.TryGetProperty("cluster", out var clusterEl) &&
                    clusterEl.TryGetProperty("status_sorter", out var statusSorterEl) &&
                    statusSorterEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in statusSorterEl.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out var statusId))
                        {
                            var obj = prop.Value;
                            var def = new DiveraStatusDefinition
                            {
                                Id = obj.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : statusId,
                                Name = obj.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                                Color = obj.TryGetProperty("color", out var colorEl) ? colorEl.GetString() ?? string.Empty : string.Empty
                            };
                            result.StatusDefinitions[def.Id] = def;
                        }
                    }
                }

                // Member-Status-Map aus cluster.status (MemberId -> StatusId)
                var memberStatusMap = new Dictionary<int, int>();
                if (dataElement.TryGetProperty("cluster", out var clusterForStatus) &&
                    clusterForStatus.TryGetProperty("status", out var clusterStatusEl) &&
                    clusterStatusEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in clusterStatusEl.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out var memberId) && prop.Value.ValueKind == JsonValueKind.Number)
                        {
                            memberStatusMap[memberId] = prop.Value.GetInt32();
                        }
                    }
                }

                // --- Mitglieder aus cluster_members ---
                if (dataElement.TryGetProperty("cluster_members", out var membersEl) &&
                    membersEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in membersEl.EnumerateObject())
                    {
                        var obj = prop.Value;
                        var memberId = obj.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                        if (memberId == 0 && int.TryParse(prop.Name, out var parsedId))
                            memberId = parsedId;

                        var member = new DiveraMember
                        {
                            Id = memberId,
                            Firstname = obj.TryGetProperty("firstname", out var fnEl) ? fnEl.GetString() ?? string.Empty : string.Empty,
                            Lastname = obj.TryGetProperty("lastname", out var lnEl) ? lnEl.GetString() ?? string.Empty : string.Empty,
                        };

                        // Status aus der Status-Map
                        if (memberStatusMap.TryGetValue(memberId, out var statusId))
                        {
                            member.StatusId = statusId;
                            if (result.StatusDefinitions.TryGetValue(statusId, out var statusDef))
                            {
                                member.StatusName = statusDef.Name;
                                member.StatusColor = statusDef.Color;
                            }
                        }

                        // Qualifikationen
                        if (obj.TryGetProperty("qualifications", out var qualsEl) && qualsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var q in qualsEl.EnumerateArray())
                            {
                                if (q.ValueKind == JsonValueKind.Number)
                                    member.QualificationIds.Add(q.GetInt32());
                            }
                        }

                        result.Members.Add(member);
                    }
                }

                // --- Alarme aus alarm.items ---
                if (dataElement.TryGetProperty("alarm", out var alarmElement) &&
                    alarmElement.TryGetProperty("items", out var itemsElement) &&
                    itemsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var alarmProp in itemsElement.EnumerateObject())
                    {
                        var obj = alarmProp.Value;
                        var alarm = new DiveraAlarm
                        {
                            Id = obj.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0,
                            Title = obj.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty,
                            Text = obj.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty,
                            Address = obj.TryGetProperty("address", out var addrEl) ? addrEl.GetString() ?? string.Empty : string.Empty,
                            Lat = obj.TryGetProperty("lat", out var latEl) && latEl.ValueKind == JsonValueKind.Number ? latEl.GetDouble() : null,
                            Lng = obj.TryGetProperty("lng", out var lngEl) && lngEl.ValueKind == JsonValueKind.Number ? lngEl.GetDouble() : null,
                            Date = obj.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.Number
                                ? DateTimeOffset.FromUnixTimeSeconds(dateEl.GetInt64()).LocalDateTime
                                : DateTime.MinValue,
                            Closed = obj.TryGetProperty("closed", out var closedEl) && closedEl.ValueKind == JsonValueKind.True,
                            Priority = obj.TryGetProperty("priority", out var prioEl) && prioEl.ValueKind == JsonValueKind.True,
                        };

                        // UCR-Rueckmeldungen parsen
                        if (obj.TryGetProperty("ucr", out var ucrEl) && ucrEl.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var ucrProp in ucrEl.EnumerateObject())
                            {
                                if (int.TryParse(ucrProp.Name, out var ucrMemberId) && ucrProp.Value.ValueKind == JsonValueKind.Number)
                                {
                                    alarm.Ucr[ucrMemberId] = ucrProp.Value.GetInt32();
                                }
                            }
                        }

                        // UCR-Details mit Mitglieds-Namen aufloesen
                        var memberLookup = result.Members.ToDictionary(m => m.Id, m => m.FullName);
                        foreach (var (ucrMemberId, ucrStatus) in alarm.Ucr)
                        {
                            alarm.UcrDetails.Add(new DiveraUcrEntry
                            {
                                MemberId = ucrMemberId,
                                MemberName = memberLookup.TryGetValue(ucrMemberId, out var name) ? name : $"#{ucrMemberId}",
                                Status = ucrStatus
                            });
                        }

                        result.Alarms.Add(alarm);
                    }
                }

                _logger.LogInformation("Divera PullAll geparst: {AlarmCount} Alarme, {MemberCount} Mitglieder",
                    result.Alarms.Count, result.Members.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Parsen der Divera PullAll-Antwort");
                return null;
            }
        }
    }
}

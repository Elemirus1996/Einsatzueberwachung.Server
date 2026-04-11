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

        // Cache fuer last-alarm Endpunkt (Fallback fuer Staffel-API-Key)
        private DiveraAlarm? _cachedLastAlarm;
        private DateTime _lastAlarmCacheTime = DateTime.MinValue;

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
            // Beide Caches invalidieren bei Konfig-Aenderung
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

        /// <summary>Leitet die Host-URL aus der konfigurierten Basis-URL ab (ohne /api/v2 oder /api Suffix).</summary>
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

        /// <summary>
        /// Ruft den letzten aktiven Alarm ueber /api/last-alarm ab.
        /// Dieser Endpunkt ist explizit fuer Web-API-Accesskey der Einheit (Staffel-API) dokumentiert
        /// und zuverlaessiger als pull/all fuer reine Alarm-Pruefungen.
        /// </summary>
        public async Task<DiveraAlarm?> GetLastAlarmAsync()
        {
            await LoadConfigIfNeededAsync();
            if (!IsConfigured) return null;

            // Cache pruefen
            if (_cachedLastAlarm != null && DateTime.UtcNow - _lastAlarmCacheTime < CurrentCacheDuration)
                return _cachedLastAlarm.Id > 0 && !_cachedLastAlarm.Closed ? _cachedLastAlarm : null;

            try
            {
                var url = $"{GetApiHostUrl()}/api/last-alarm?accesskey={_accessKey}";
                _logger.LogInformation("Divera LastAlarm: GET {BaseUrl}/api/last-alarm", GetApiHostUrl());

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Divera LastAlarm fehlgeschlagen: HTTP {StatusCode}", response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Divera LastAlarm Antwort: {Json}", json.Length > 1000 ? json[..1000] + "\u2026" : json);

                var alarm = ParseLastAlarmResponse(json);

                // Auch "kein aktiver Alarm" cachen um unnoetige API-Calls zu vermeiden
                _cachedLastAlarm = alarm ?? new DiveraAlarm { Closed = true };
                _lastAlarmCacheTime = DateTime.UtcNow;

                if (alarm != null)
                    DataChanged?.Invoke();

                return alarm;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Abrufen von Divera LastAlarm");
                return null;
            }
        }

        private DiveraAlarm? ParseLastAlarmResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
                    return null;

                if (!root.TryGetProperty("data", out var dataEl))
                    return null;

                // data ist null wenn kein aktiver Alarm vorhanden
                if (dataEl.ValueKind == JsonValueKind.Null || dataEl.ValueKind == JsonValueKind.Undefined)
                    return null;

                // Alarm-Felder koennen direkt in data liegen (v1) oder in data.alarm (v2-Stil)
                var alarmEl = dataEl;
                if (dataEl.TryGetProperty("alarm", out var innerEl) && innerEl.ValueKind == JsonValueKind.Object)
                    alarmEl = innerEl;

                if (alarmEl.ValueKind != JsonValueKind.Object)
                    return null;

                if (!alarmEl.TryGetProperty("id", out var idEl) || idEl.GetInt32() == 0)
                    return null;

                // closed: sowohl als Boolean als auch als Integer (0 = offen, 1 = geschlossen)
                bool closed = false;
                if (alarmEl.TryGetProperty("closed", out var closedEl))
                {
                    closed = closedEl.ValueKind == JsonValueKind.True ||
                             (closedEl.ValueKind == JsonValueKind.Number && closedEl.GetInt64() != 0);
                }

                if (closed) return null;

                var alarm = new DiveraAlarm
                {
                    Id = idEl.GetInt32(),
                    ForeignId = alarmEl.TryGetProperty("foreign_id", out var fidEl) ? fidEl.GetString() ?? string.Empty : string.Empty,
                    Title = alarmEl.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty,
                    Text = alarmEl.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty,
                    Address = alarmEl.TryGetProperty("address", out var addrEl) ? addrEl.GetString() ?? string.Empty : string.Empty,
                    Lat = alarmEl.TryGetProperty("lat", out var latEl) && latEl.ValueKind == JsonValueKind.Number ? latEl.GetDouble() : null,
                    Lng = alarmEl.TryGetProperty("lng", out var lngEl) && lngEl.ValueKind == JsonValueKind.Number ? lngEl.GetDouble() : null,
                    Date = alarmEl.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeSeconds(dateEl.GetInt64()).LocalDateTime
                        : DateTime.MinValue,
                    Closed = false,
                    Priority = alarmEl.TryGetProperty("priority", out var prioEl) && prioEl.ValueKind == JsonValueKind.True,
                    Caller = alarmEl.TryGetProperty("caller", out var callerEl) ? callerEl.GetString() ?? string.Empty : string.Empty,
                    Remark = alarmEl.TryGetProperty("remark", out var remarkEl) ? remarkEl.GetString() ?? string.Empty : string.Empty,
                };

                // ucr_addressed: einfaches Array der adressierten user_cluster_relation IDs
                var addressedIds = new System.Collections.Generic.HashSet<int>();
                if (alarmEl.TryGetProperty("ucr_addressed", out var addressedEl) && addressedEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var uid in addressedEl.EnumerateArray())
                    {
                        if (uid.ValueKind == JsonValueKind.Number)
                        {
                            var id = uid.GetInt32();
                            addressedIds.Add(id);
                            alarm.AddressedUserIds.Add(id);
                        }
                    }
                }

                // ucr_answered: {statusId: {userId: {ts, note}}} — KEIN Array, sondern verschachteltes Object!
                // Outer key = org-spezifische Status-ID (z.B. 56298)
                // Inner key = user_cluster_relation_id
                var answeredIds = new System.Collections.Generic.HashSet<int>();
                if (alarmEl.TryGetProperty("ucr_answered", out var answeredEl) && answeredEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var statusProp in answeredEl.EnumerateObject())
                    {
                        if (!int.TryParse(statusProp.Name, out var statusId)) continue;
                        if (statusProp.Value.ValueKind != JsonValueKind.Object) continue;

                        foreach (var userProp in statusProp.Value.EnumerateObject())
                        {
                            if (!int.TryParse(userProp.Name, out var userId)) continue;
                            if (answeredIds.Contains(userId)) continue; // Duplikat verhindern

                            answeredIds.Add(userId);
                            alarm.Ucr[userId] = statusId;
                            alarm.UcrDetails.Add(new DiveraUcrEntry
                            {
                                MemberId = userId,
                                MemberName = $"#{userId}",
                                Status = statusId  // org-spezifische Status-ID
                            });
                        }
                    }
                }

                // Adressierte die NICHT geantwortet haben → Status 0
                foreach (var addressedId in addressedIds)
                {
                    if (!answeredIds.Contains(addressedId))
                    {
                        alarm.Ucr[addressedId] = 0;
                        alarm.UcrDetails.Add(new DiveraUcrEntry
                        {
                            MemberId = addressedId,
                            MemberName = $"#{addressedId}",
                            Status = 0
                        });
                    }
                }

                _logger.LogInformation("Divera LastAlarm geparst: ID={Id}, Titel='{Title}', Adressiert={Addressed}, Geantwortet={Answered}",
                    alarm.Id, alarm.Title, addressedIds.Count, answeredIds.Count);

                return alarm;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Parsen der Divera LastAlarm-Antwort");
                return null;
            }
        }

        public async Task<List<DiveraAlarm>> GetActiveAlarmsAsync()
        {
            // Primaer: Alarme aus pull/all (enthalten UCR mit Status-Details)
            var pull = await PullAllAsync();
            var alarms = pull?.Alarms.Where(a => !a.Closed).ToList() ?? new List<DiveraAlarm>();

            // Fallback: Wenn pull/all keine Alarme liefert, last-alarm Endpunkt probieren.
            if (!alarms.Any())
            {
                var lastAlarm = await GetLastAlarmAsync();
                if (lastAlarm != null)
                {
                    // Status-Namen aus pull/all StatusDefinitions aufloesen (falls vorhanden)
                    if (pull?.StatusDefinitions?.Count > 0)
                    {
                        foreach (var ucrEntry in lastAlarm.UcrDetails)
                        {
                            if (pull.StatusDefinitions.TryGetValue(ucrEntry.Status, out var statusDef))
                                ucrEntry.StatusName = statusDef.Name;
                        }
                    }
                    // Member-Namen aus pull/all Members aufloesen (falls vorhanden)
                    if (pull?.Members?.Count > 0)
                    {
                        var memberLookup = pull.Members.ToDictionary(m => m.Id, m => m.FullName);
                        foreach (var ucrEntry in lastAlarm.UcrDetails)
                            if (memberLookup.TryGetValue(ucrEntry.MemberId, out var name) && !string.IsNullOrWhiteSpace(name))
                                ucrEntry.MemberName = name;
                        // Weiterer Fallback: Stammdaten-Lookup erfolgt in der Razor-Page (DiveraStatus.razor)
                    }

                    alarms.Add(lastAlarm);
                }
            }

            return alarms;
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

                _logger.LogDebug("Divera PullAll: Data-Schluessel: {Keys}",
                    string.Join(", ", dataElement.EnumerateObject().Select(p => p.Name)));

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
                            ForeignId = obj.TryGetProperty("foreign_id", out var fidEl2) ? fidEl2.GetString() ?? string.Empty : string.Empty,
                            Title = obj.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? string.Empty : string.Empty,
                            Text = obj.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty,
                            Address = obj.TryGetProperty("address", out var addrEl) ? addrEl.GetString() ?? string.Empty : string.Empty,
                            Lat = obj.TryGetProperty("lat", out var latEl) && latEl.ValueKind == JsonValueKind.Number ? latEl.GetDouble() : null,
                            Lng = obj.TryGetProperty("lng", out var lngEl) && lngEl.ValueKind == JsonValueKind.Number ? lngEl.GetDouble() : null,
                            Date = obj.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.Number
                                ? DateTimeOffset.FromUnixTimeSeconds(dateEl.GetInt64()).LocalDateTime
                                : DateTime.MinValue,
                            // closed: Boolean ODER Integer (0=offen, 1=geschlossen)
                            Closed = obj.TryGetProperty("closed", out var closedEl) &&
                                     (closedEl.ValueKind == JsonValueKind.True ||
                                      (closedEl.ValueKind == JsonValueKind.Number && closedEl.GetInt64() != 0)),
                            Priority = obj.TryGetProperty("priority", out var prioEl) && prioEl.ValueKind == JsonValueKind.True,
                            Caller = obj.TryGetProperty("caller", out var callerEl2) ? callerEl2.GetString() ?? string.Empty : string.Empty,
                            Remark = obj.TryGetProperty("remark", out var remarkEl2) ? remarkEl2.GetString() ?? string.Empty : string.Empty,
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

        /// <summary>Holt rohe JSON-Antworten beider Endpunkte fuer Diagnose-Zwecke.</summary>
        public async Task<Dictionary<string, string>> GetRawDiagnosticAsync()
        {
            await LoadConfigIfNeededAsync();

            var result = new Dictionary<string, string>();

            if (!IsConfigured)
            {
                result["error"] = "Divera nicht konfiguriert (DiveraEnabled=false oder kein API-Key).";
                return result;
            }

            // pull/all
            try
            {
                var pullUrl = $"{_baseUrl}/pull/all?accesskey={_accessKey}";
                var pullResp = await _httpClient.GetAsync(pullUrl);
                var pullJson = await pullResp.Content.ReadAsStringAsync();
                // JSON kuerzen wenn zu lang, aber Struktur sichtbar lassen
                result["pull_all_status"] = ((int)pullResp.StatusCode).ToString();
                result["pull_all_url"] = $"{_baseUrl}/pull/all";
                result["pull_all_json"] = pullJson.Length > 4000 ? pullJson[..4000] + "\n…(gekuerzt)" : pullJson;
            }
            catch (Exception ex)
            {
                result["pull_all_error"] = ex.Message;
            }

            // last-alarm
            try
            {
                var lastUrl = $"{GetApiHostUrl()}/api/last-alarm?accesskey={_accessKey}";
                var lastResp = await _httpClient.GetAsync(lastUrl);
                var lastJson = await lastResp.Content.ReadAsStringAsync();
                result["last_alarm_status"] = ((int)lastResp.StatusCode).ToString();
                result["last_alarm_url"] = $"{GetApiHostUrl()}/api/last-alarm";
                result["last_alarm_json"] = lastJson.Length > 4000 ? lastJson[..4000] + "\n…(gekuerzt)" : lastJson;
            }
            catch (Exception ex)
            {
                result["last_alarm_error"] = ex.Message;
            }

            return result;
        }
    }
}

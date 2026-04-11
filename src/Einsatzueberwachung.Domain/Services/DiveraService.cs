// Divera 24/7 API-Service
// Dokumentation: https://api.divera247.com/ (Swagger UI)
// Wichtig: Einheiten-Zugangscode verwenden, NICHT den persoenlichen Benutzer-API-Schluessel!
// Einheiten-Zugangscode: Divera → Verwaltung → Schnittstellen → API

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models.Divera;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public class DiveraService : IDiveraService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DiveraService>? _logger;
        private readonly string _baseUrl;
        private readonly string _accessKey;

        // 60-Sekunden-Cache fuer pull/all (verhindert zu viele API-Anfragen)
        private DiveraPullResponse? _cachedPullResponse;
        private DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public bool IsConfigured { get; }

        public event Action? DataChanged;

        public DiveraService(HttpClient httpClient, IConfiguration configuration, ILogger<DiveraService>? logger = null)
        {
            _httpClient = httpClient;
            _logger = logger;
            _httpClient.Timeout = TimeSpan.FromSeconds(15);

            var enabled = string.Equals(configuration["Divera:Enabled"], "true", StringComparison.OrdinalIgnoreCase)
                       || configuration["Divera:Enabled"] == "1";
            _baseUrl = configuration["Divera:BaseUrl"] ?? "https://app.divera247.com/api/v2";
            _accessKey = configuration["Divera:AccessKey"] ?? string.Empty;

            IsConfigured = enabled && !string.IsNullOrWhiteSpace(_accessKey);

            if (IsConfigured)
            {
                _logger?.LogInformation("Divera 24/7 Service initialisiert. BaseUrl: {BaseUrl}", _baseUrl);
            }
            else
            {
                _logger?.LogDebug("Divera 24/7 Service nicht konfiguriert (Enabled={Enabled}, AccessKey vorhanden={HasKey})",
                    enabled, !string.IsNullOrWhiteSpace(_accessKey));
            }
        }

        public async Task<DiveraPullResponse?> PullAllAsync()
        {
            if (!IsConfigured) return null;

            // Cache pruefen
            if (_cachedPullResponse != null && DateTime.Now - _cacheTime < CacheDuration)
            {
                _logger?.LogDebug("Divera pull/all: Returning cached response");
                return _cachedPullResponse;
            }

            try
            {
                var url = $"{_baseUrl}/pull/all?accesskey={_accessKey}";
                _logger?.LogInformation("Divera pull/all: GET {Url}", MaskKey(url));

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("Divera API Fehler: {StatusCode} {Reason}", response.StatusCode, response.ReasonPhrase);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogDebug("Divera pull/all Antwort ({Len} Bytes)", json.Length);

                var raw = JsonSerializer.Deserialize<DiveraApiResponse>(json, JsonOptions);

                if (raw?.Success != true || raw.Data == null)
                {
                    _logger?.LogWarning("Divera API: success=false oder data=null. JSON: {Json}",
                        json.Length > 500 ? json[..500] : json);
                    return null;
                }

                var result = MapToDomain(raw);

                // Cache aktualisieren
                _cachedPullResponse = result;
                _cacheTime = DateTime.Now;

                DataChanged?.Invoke();

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Divera pull/all: Ausnahme beim Abrufen der Daten");
                return null;
            }
        }

        public async Task<List<DiveraAlarm>> GetActiveAlarmsAsync()
        {
            var pull = await PullAllAsync();
            return pull?.AktiveAlarme ?? new List<DiveraAlarm>();
        }

        public async Task<DiveraAlarm?> GetAlarmByIdAsync(int alarmId)
        {
            if (!IsConfigured) return null;

            // Zuerst im Cache suchen
            if (_cachedPullResponse != null && DateTime.Now - _cacheTime < CacheDuration)
            {
                var cached = _cachedPullResponse.AlleAlarme.FirstOrDefault(a => a.Id == alarmId);
                if (cached != null) return cached;
            }

            // Direkter API-Aufruf fuer einzelnen Alarm
            try
            {
                var url = $"{_baseUrl}/alarms/{alarmId}?accesskey={_accessKey}";
                _logger?.LogInformation("Divera: GET Alarm {AlarmId}", alarmId);

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var raw = JsonSerializer.Deserialize<DiveraSingleAlarmResponse>(json, JsonOptions);

                if (raw?.Data == null) return null;

                return MapAlarmRaw(raw.Data, new Dictionary<string, DiveraUserRaw>());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Divera: Fehler beim Abrufen von Alarm {AlarmId}", alarmId);
                return null;
            }
        }

        public async Task<List<DiveraMember>> GetMembersWithStatusAsync()
        {
            var pull = await PullAllAsync();
            return pull?.Mitglieder ?? new List<DiveraMember>();
        }

        public async Task<List<DiveraMember>> GetAvailableMembersAsync()
        {
            var members = await GetMembersWithStatusAsync();
            return members.Where(m => m.StatusId == 1 || m.StatusId == 2).ToList();
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (!IsConfigured) return false;

            try
            {
                var url = $"{_baseUrl}/pull/all?accesskey={_accessKey}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("Divera Verbindungstest fehlgeschlagen: {StatusCode}", response.StatusCode);
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                var raw = JsonSerializer.Deserialize<DiveraApiResponse>(json, JsonOptions);
                return raw?.Success == true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Divera Verbindungstest: Ausnahme");
                return false;
            }
        }

        // ── Mapping von Roh-API-Antwort → Domain-Modelle ─────────────────────

        private static DiveraPullResponse MapToDomain(DiveraApiResponse raw)
        {
            var result = new DiveraPullResponse();
            var data = raw.Data!;

            result.EinheitName = data.Cluster?.Name ?? string.Empty;

            // Status-Definitionen
            var statusDefs = new Dictionary<int, DiveraStatusDefinition>();
            if (data.Cluster?.StatusSorter != null)
            {
                foreach (var s in data.Cluster.StatusSorter)
                {
                    var def = new DiveraStatusDefinition
                    {
                        Id = s.Id,
                        Name = s.Name ?? string.Empty,
                        Shortname = s.Shortname ?? string.Empty,
                        Color = s.Color ?? string.Empty,
                        ShowInAvailability = s.ShowInAvailability
                    };
                    result.StatusDefinitionen.Add(def);
                    statusDefs[s.Id] = def;
                }
            }

            // Mitglieder
            var userItems = data.User?.Items ?? new Dictionary<string, DiveraUserRaw>();
            var memberStatus = data.Cluster?.Status ?? new Dictionary<string, int>();

            foreach (var kvp in userItems)
            {
                var u = kvp.Value;
                int userId = u.Id;
                int statusId = 0;

                // Verfuegbarkeitsstatus aus cluster.status auslesen
                if (memberStatus.TryGetValue(userId.ToString(), out var sid))
                {
                    statusId = sid;
                }

                var statusText = "Kein Status";
                var statusColor = string.Empty;
                if (statusId > 0 && statusDefs.TryGetValue(statusId, out var sDef))
                {
                    statusText = sDef.Name;
                    statusColor = sDef.Color;
                }

                result.Mitglieder.Add(new DiveraMember
                {
                    Id = userId,
                    Firstname = u.Firstname ?? string.Empty,
                    Lastname = u.Lastname ?? string.Empty,
                    Qualifications = u.Stdqualification ?? new List<int>(),
                    StatusId = statusId,
                    StatusText = statusText,
                    StatusColor = statusColor
                });
            }

            // Mitglieder nach Nachnamen sortieren
            result.Mitglieder = result.Mitglieder.OrderBy(m => m.Lastname).ThenBy(m => m.Firstname).ToList();

            // Alarme
            var alarmItems = data.Alarm?.Items ?? new Dictionary<string, DiveraAlarmRaw>();
            foreach (var kvp in alarmItems)
            {
                var alarm = MapAlarmRaw(kvp.Value, userItems);
                result.AlleAlarme.Add(alarm);
            }

            // Nach Datum absteigend sortieren (neueste zuerst)
            result.AlleAlarme = result.AlleAlarme.OrderByDescending(a => a.Date).ToList();
            result.AktiveAlarme = result.AlleAlarme.Where(a => !a.Closed).ToList();

            return result;
        }

        private static DiveraAlarm MapAlarmRaw(DiveraAlarmRaw raw, Dictionary<string, DiveraUserRaw> userItems)
        {
            var alarm = new DiveraAlarm
            {
                Id = raw.Id,
                Title = raw.Title ?? string.Empty,
                Text = raw.Text ?? string.Empty,
                Address = raw.Address ?? string.Empty,
                Closed = raw.Closed,
                Priority = raw.Priority,
                Type = raw.Type ?? string.Empty,
                ForeignId = raw.ForeignId ?? string.Empty,
                AssignedVehicleIds = raw.AssignedVehicleIds ?? new List<int>()
            };

            // Alarmzeit aus Unix-Timestamp
            if (raw.Date > 0)
            {
                alarm.Date = DateTimeOffset.FromUnixTimeSeconds(raw.Date).LocalDateTime;
            }

            // GPS-Koordinaten (Divera liefert diese als Strings oder Zahlen)
            alarm.Lat = ParseCoordinate(raw.LatRaw);
            alarm.Lng = ParseCoordinate(raw.LngRaw);

            // Rueckmeldungen (UCR) mappen
            if (raw.Ucr != null)
            {
                foreach (var ucrKvp in raw.Ucr)
                {
                    if (!int.TryParse(ucrKvp.Key, out var userId)) continue;

                    var ucrRaw = ucrKvp.Value;
                    var entry = new DiveraUcrEntry
                    {
                        UserId = userId,
                        Status = ucrRaw.Status,
                        Note = ucrRaw.Note ?? string.Empty,
                        Ts = ucrRaw.Ts
                    };

                    // Name aus Mitgliederliste befuellen
                    if (userItems.TryGetValue(userId.ToString(), out var user))
                    {
                        entry.MemberName = $"{user.Firstname} {user.Lastname}".Trim();
                    }
                    else
                    {
                        entry.MemberName = $"Mitglied #{userId}";
                    }

                    alarm.Rueckmeldungen.Add(entry);
                }

                // Rueckmeldungen sortieren: Komme zuerst, dann Vielleicht, dann Komme nicht
                alarm.Rueckmeldungen = alarm.Rueckmeldungen.OrderBy(r => r.Status).ToList();
            }

            return alarm;
        }

        private static double? ParseCoordinate(JsonElement? element)
        {
            if (element == null) return null;

            return element.Value.ValueKind switch
            {
                JsonValueKind.Number => element.Value.TryGetDouble(out var d) ? d : null,
                JsonValueKind.String => double.TryParse(
                    element.Value.GetString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var d2) ? d2 : null,
                _ => null
            };
        }

        private static string MaskKey(string url)
        {
            // API-Key im Log maskieren
            var idx = url.IndexOf("accesskey=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return url;
            return url[..(idx + 10)] + "***";
        }

        // ── Roh-API-Modelle (nur fuer JSON-Deserialisierung) ─────────────────

        private sealed class DiveraApiResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("data")]
            public DiveraDataRaw? Data { get; set; }
        }

        private sealed class DiveraSingleAlarmResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("data")]
            public DiveraAlarmRaw? Data { get; set; }
        }

        private sealed class DiveraDataRaw
        {
            [JsonPropertyName("cluster")]
            public DiveraClusterRaw? Cluster { get; set; }

            [JsonPropertyName("alarm")]
            public DiveraAlarmCollectionRaw? Alarm { get; set; }

            [JsonPropertyName("user")]
            public DiveraUserCollectionRaw? User { get; set; }
        }

        private sealed class DiveraClusterRaw
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            /// <summary>
            /// Aktueller Verfuegbarkeitsstatus aller Mitglieder: UserId (string) -> StatusId (int)
            /// </summary>
            [JsonPropertyName("status")]
            public Dictionary<string, int>? Status { get; set; }

            [JsonPropertyName("status_sorter")]
            public List<DiveraStatusSorterRaw>? StatusSorter { get; set; }
        }

        private sealed class DiveraStatusSorterRaw
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("shortname")]
            public string? Shortname { get; set; }

            [JsonPropertyName("color")]
            public string? Color { get; set; }

            [JsonPropertyName("show_in_availability")]
            public bool ShowInAvailability { get; set; }
        }

        private sealed class DiveraAlarmCollectionRaw
        {
            /// <summary>
            /// Alarme als Dictionary: AlarmId (string) -> AlarmDaten
            /// </summary>
            [JsonPropertyName("items")]
            public Dictionary<string, DiveraAlarmRaw>? Items { get; set; }
        }

        private sealed class DiveraAlarmRaw
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("text")]
            public string? Text { get; set; }

            [JsonPropertyName("address")]
            public string? Address { get; set; }

            /// <summary>
            /// Breitengrad - kann String oder Zahl sein
            /// </summary>
            [JsonPropertyName("lat")]
            public JsonElement? LatRaw { get; set; }

            /// <summary>
            /// Laengsgrad - kann String oder Zahl sein
            /// </summary>
            [JsonPropertyName("lng")]
            public JsonElement? LngRaw { get; set; }

            /// <summary>
            /// Alarmzeit als Unix-Timestamp (Sekunden)
            /// </summary>
            [JsonPropertyName("date")]
            public long Date { get; set; }

            [JsonPropertyName("closed")]
            public bool Closed { get; set; }

            [JsonPropertyName("priority")]
            public bool Priority { get; set; }

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("foreign_id")]
            public string? ForeignId { get; set; }

            [JsonPropertyName("assigned_vehicle_ids")]
            public List<int>? AssignedVehicleIds { get; set; }

            /// <summary>
            /// Rueckmeldungen: UserId (string) -> UcrDaten
            /// </summary>
            [JsonPropertyName("ucr")]
            public Dictionary<string, DiveraUcrRaw>? Ucr { get; set; }
        }

        private sealed class DiveraUcrRaw
        {
            [JsonPropertyName("status")]
            public int Status { get; set; }

            [JsonPropertyName("note")]
            public string? Note { get; set; }

            [JsonPropertyName("ts")]
            public long Ts { get; set; }
        }

        private sealed class DiveraUserCollectionRaw
        {
            /// <summary>
            /// Mitglieder als Dictionary: UserId (string) -> Mitgliedsdaten
            /// </summary>
            [JsonPropertyName("items")]
            public Dictionary<string, DiveraUserRaw>? Items { get; set; }
        }

        private sealed class DiveraUserRaw
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("firstname")]
            public string? Firstname { get; set; }

            [JsonPropertyName("lastname")]
            public string? Lastname { get; set; }

            [JsonPropertyName("stdqualification")]
            public List<int>? Stdqualification { get; set; }
        }
    }
}

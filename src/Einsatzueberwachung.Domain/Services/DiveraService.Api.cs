using Einsatzueberwachung.Domain.Models.Divera;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class DiveraService
    {
        public async Task<DiveraPullResponse?> PullAllAsync()
        {
            await LoadConfigIfNeededAsync();

            if (!IsConfigured)
            {
                _logger.LogDebug("Divera: Nicht konfiguriert — PullAll wird uebersprungen.");
                return null;
            }

            if (_cachedPull != null && DateTime.UtcNow - _cacheTime < PullCacheDuration)
            {
                return _cachedPull;
            }

            // Nur eine Abfrage gleichzeitig – alle wartenden Seiten bekommen danach dasselbe Ergebnis
            await _fetchLock.WaitAsync();
            try
            {
                if (_cachedPull != null && DateTime.UtcNow - _cacheTime < PullCacheDuration)
                    return _cachedPull;
                if (InBackoff)
                    return _cachedPull;

                HttpResponseMessage? response = null;
                try
                {
                    var url = $"{_baseUrl}/pull/all?accesskey={_accessKey}";
                    _logger.LogInformation("Divera PullAll: GET {Url}", _baseUrl + "/pull/all");

                    response = await _httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Divera PullAll fehlgeschlagen: HTTP {StatusCode}", response.StatusCode);
                        RecordFailure(response);
                        return _cachedPull;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var result = ParsePullAllResponse(json);
                    if (result == null)
                    {
                        RecordFailure(null);
                        return _cachedPull;
                    }

                    await AnreichernAsync(result, result.Alarms);
                    RecordSuccess();
                    _cachedPull = result;
                    _cacheTime = DateTime.UtcNow;
                    if (result.StatusDefinitions.Count > 0)
                        _statusDefinitionen = result.StatusDefinitions.Values.OrderBy(d => d.Name).ToList();
                    DataChanged?.Invoke();
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler beim Abrufen von Divera PullAll");
                    RecordFailure(response);
                    return _cachedPull;
                }
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        public async Task<DiveraAlarm?> GetLastAlarmAsync()
        {
            await LoadConfigIfNeededAsync();
            if (!IsConfigured) return null;

            if (_cachedLastAlarm != null && DateTime.UtcNow - _lastAlarmCacheTime < LastAlarmCacheDuration)
                return _cachedLastAlarm.Id > 0 && !_cachedLastAlarm.Closed ? _cachedLastAlarm : null;

            await _fetchLock.WaitAsync();
            try
            {
                if (_cachedLastAlarm != null && DateTime.UtcNow - _lastAlarmCacheTime < LastAlarmCacheDuration)
                    return _cachedLastAlarm.Id > 0 && !_cachedLastAlarm.Closed ? _cachedLastAlarm : null;
                if (InBackoff)
                    return _cachedLastAlarm is { Id: > 0, Closed: false } ? _cachedLastAlarm : null;

                HttpResponseMessage? response = null;
                try
                {
                    var url = $"{GetApiHostUrl()}/api/last-alarm?accesskey={_accessKey}";
                    _logger.LogInformation("Divera LastAlarm: GET {BaseUrl}/api/last-alarm", GetApiHostUrl());

                    response = await _httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Divera LastAlarm fehlgeschlagen: HTTP {StatusCode}", response.StatusCode);
                        RecordFailure(response);
                        return null;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var alarm = ParseLastAlarmResponse(json);
                    if (alarm != null)
                        await AnreichernAsync(_cachedPull, new[] { alarm });

                    RecordSuccess();
                    _cachedLastAlarm = alarm ?? new DiveraAlarm { Closed = true };
                    _lastAlarmCacheTime = DateTime.UtcNow;

                    if (alarm != null)
                        DataChanged?.Invoke();

                    return alarm;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler beim Abrufen von Divera LastAlarm");
                    RecordFailure(response);
                    return null;
                }
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        public async Task<List<DiveraAlarm>> GetActiveAlarmsAsync()
        {
            var pull = await PullAllAsync();
            var alarms = pull?.Alarms.Where(a => !a.Closed).ToList() ?? new List<DiveraAlarm>();

            if (!alarms.Any())
            {
                var lastAlarm = await GetLastAlarmAsync();
                if (lastAlarm != null)
                    alarms.Add(lastAlarm);
            }

            return alarms;
        }

        public async Task<DiveraAlarm?> GetAlarmByIdAsync(int alarmId)
        {
            var pull = await PullAllAsync();
            return pull?.Alarms.FirstOrDefault(a => a.Id == alarmId)
                ?? (_cachedLastAlarm is { } last && last.Id == alarmId ? last : null);
        }

        public async Task<List<DiveraMember>> GetMembersWithStatusAsync()
        {
            var pull = await PullAllAsync();
            if (pull == null)
                return new List<DiveraMember>();

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

        public async Task<Dictionary<string, string>> GetRawDiagnosticAsync()
        {
            await LoadConfigIfNeededAsync();

            var result = new Dictionary<string, string>();

            if (!IsConfigured)
            {
                result["error"] = "Divera nicht konfiguriert (DiveraEnabled=false oder kein API-Key).";
                return result;
            }

            try
            {
                var pullUrl = $"{_baseUrl}/pull/all?accesskey={_accessKey}";
                var pullResp = await _httpClient.GetAsync(pullUrl);
                var pullJson = await pullResp.Content.ReadAsStringAsync();
                result["pull_all_status"] = ((int)pullResp.StatusCode).ToString();
                result["pull_all_url"] = $"{_baseUrl}/pull/all";
                result["pull_all_json"] = pullJson.Length > 4000 ? pullJson[..4000] + "\n…(gekuerzt)" : pullJson;
            }
            catch (Exception ex)
            {
                result["pull_all_error"] = ex.Message;
            }

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

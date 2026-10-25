using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Einsatzueberwachung.LiveTracking.Services
{
    /// <summary>Ergebnis des Verbindungstests – mit Ursache statt nur „nicht erreichbar“.</summary>
    public enum ConnectionTestResult
    {
        Ok,
        Unreachable,
        CertificateRejected,
        Unauthorized,
        ServerError,
    }

    /// <summary>
    /// Verbindung zur Einsatzueberwachung (1.10): Verbindungscode mit Adresse, API-Schluessel und Zertifikat-Fingerabdruck,
    /// Positionen ueber einen Puffer – bei Netzausfall werden sie mit Originalzeit nachgesendet statt verworfen.
    /// </summary>
    public class ServerApiClient : IDisposable
    {
        public const string CodePrefix = "EUL1.";
        private const string ApiKeyHeader = "X-Api-Key";
        private const int MaxQueuedLocations = 10_000;
        private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

        private readonly ConcurrentQueue<PendingLocation> _queue = new();
        private readonly SemaphoreSlim _flushLock = new(1, 1);
        private HttpClient? _httpClient;
        private string _apiKey = string.Empty;
        private string? _certificateSha256;
        private CancellationTokenSource _retryCts = new();
        private TimeSpan _retryDelay = MinRetryDelay;
        private bool _disposed;

        public string ServerUrl { get; private set; } = string.Empty;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerUrl);
        public bool HasApiKey => _apiKey.Length > 0;
        public int PendingCount => _queue.Count;

        public event Action<string>? StatusChanged;
        /// <summary>Position wurde vom Server angenommen (Halsband-ID).</summary>
        public event Action<string>? LocationDelivered;
        /// <summary>Position wurde endgueltig verworfen (Server lehnt sie ab oder Puffer voll).</summary>
        public event Action<string>? LocationDropped;
        public event Action<int>? PendingCountChanged;

        /// <summary>Verbindungscode aus den Einstellungen oder – wie frueher – nur eine Adresse.</summary>
        public bool Configure(string input)
        {
            input = input.Trim();
            string url;
            if (input.StartsWith(CodePrefix, StringComparison.Ordinal))
            {
                var info = ParseConnectionCode(input);
                if (info is null)
                {
                    StatusChanged?.Invoke("Verbindungscode ist ungültig – bitte erneut aus den Einstellungen kopieren.");
                    return false;
                }
                url = info.ServerUrl;
                _apiKey = info.ApiKey;
                _certificateSha256 = info.CertificateSha256;
            }
            else
            {
                url = input;
                _apiKey = string.Empty;
                _certificateSha256 = null;
            }

            ServerUrl = url.TrimEnd('/');
            _httpClient?.Dispose();
            _httpClient = CreateHttpClient(_certificateSha256);
            StatusChanged?.Invoke($"Server konfiguriert: {ServerUrl}" +
                (_certificateSha256 is null ? string.Empty : " (mit Zertifikat-Fingerabdruck)"));
            if (!HasApiKey)
            {
                StatusChanged?.Invoke("Hinweis: Ohne Verbindungscode fehlt der API-Schlüssel – ab Version 1.10 lehnt der Server Positionen dann ab.");
            }
            return true;
        }

        public void Disconnect()
        {
            ServerUrl = string.Empty;
            _retryCts.Cancel();
            StatusChanged?.Invoke("Server-Verbindung getrennt.");
        }

        /// <summary>Position in den Puffer legen und sofort zu senden versuchen.</summary>
        public void EnqueueCollarLocation(string collarId, string collarName, double latitude, double longitude, int? batteryLevel = null)
        {
            _queue.Enqueue(new PendingLocation(collarId, collarName, latitude, longitude, batteryLevel, DateTimeOffset.UtcNow));
            while (_queue.Count > MaxQueuedLocations && _queue.TryDequeue(out var dropped))
            {
                LocationDropped?.Invoke(dropped.CollarId);
            }
            PendingCountChanged?.Invoke(_queue.Count);
            _ = FlushAsync();
        }

        /// <summary>Puffer der Reihe nach senden; bei Netzfehler spaeter erneut (5 s, dann laenger bis 60 s).</summary>
        public async Task FlushAsync()
        {
            if (!IsConfigured || _httpClient is null) return;
            if (!await _flushLock.WaitAsync(0)) return; // laeuft schon

            var networkError = false;
            try
            {
                while (_queue.TryPeek(out var next))
                {
                    var outcome = await TrySendAsync(next);
                    if (outcome == SendOutcome.Retry)
                    {
                        networkError = true;
                        break;
                    }

                    _queue.TryDequeue(out _);
                    if (outcome == SendOutcome.Delivered) LocationDelivered?.Invoke(next.CollarId);
                    else LocationDropped?.Invoke(next.CollarId);
                    PendingCountChanged?.Invoke(_queue.Count);
                }
            }
            finally
            {
                _flushLock.Release();
            }

            if (networkError)
            {
                ScheduleRetry();
            }
            else
            {
                _retryDelay = MinRetryDelay;
            }
        }

        private void ScheduleRetry()
        {
            _retryCts.Cancel();
            _retryCts = new CancellationTokenSource();
            var token = _retryCts.Token;
            var delay = _retryDelay;
            _retryDelay = TimeSpan.FromSeconds(Math.Min(MaxRetryDelay.TotalSeconds, _retryDelay.TotalSeconds * 2));
            StatusChanged?.Invoke($"Server nicht erreichbar – {PendingCount} Positionen im Puffer, neuer Versuch in {delay.TotalSeconds:0} s.");
            _ = Task.Delay(delay, token).ContinueWith(t =>
            {
                if (!t.IsCanceled) _ = FlushAsync();
            }, TaskScheduler.Default);
        }

        private async Task<SendOutcome> TrySendAsync(PendingLocation location)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/api/CollarWebhook/location")
                {
                    Content = JsonContent.Create(new
                    {
                        Id = location.CollarId,
                        CollarName = location.CollarName,
                        Coordinates = new { Lat = location.Latitude, Lng = location.Longitude },
                        BatteryLevel = location.BatteryLevel,
                        Timestamp = location.Timestamp,
                    }),
                };
                AddApiKey(request);
                using var response = await _httpClient!.SendAsync(request);

                if (response.IsSuccessStatusCode) return SendOutcome.Delivered;
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Behalten: nach neuem Verbindungscode werden die Positionen nachgesendet
                    StatusChanged?.Invoke("API-Schlüssel ungültig – neuen Verbindungscode aus den Einstellungen eintragen.");
                    return SendOutcome.Retry;
                }
                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    StatusChanged?.Invoke($"Server-Fehler: {(int)response.StatusCode}");
                    return SendOutcome.Retry;
                }

                // 400 & Co.: Position wird nie angenommen (z. B. älter als 24 h) – verwerfen statt endlos wiederholen
                StatusChanged?.Invoke($"Position abgelehnt ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
                return SendOutcome.Rejected;
            }
            catch (HttpRequestException ex)
            {
                StatusChanged?.Invoke($"Verbindungsfehler: {Describe(ex)}");
                return SendOutcome.Retry;
            }
            catch (TaskCanceledException)
            {
                return SendOutcome.Retry;
            }
        }

        public async Task<ConnectionTestResult> TestConnectionAsync()
        {
            if (!IsConfigured || _httpClient is null) return ConnectionTestResult.Unreachable;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{ServerUrl}/api/CollarWebhook/collars");
                AddApiKey(request);
                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode) return ConnectionTestResult.Ok;
                if (response.StatusCode == HttpStatusCode.Unauthorized) return ConnectionTestResult.Unauthorized;
                return ConnectionTestResult.ServerError;
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
            {
                StatusChanged?.Invoke($"Zertifikat abgelehnt: {Describe(ex)}");
                return ConnectionTestResult.CertificateRejected;
            }
            catch (HttpRequestException ex)
            {
                StatusChanged?.Invoke($"Verbindungsfehler: {Describe(ex)}");
                return ConnectionTestResult.Unreachable;
            }
            catch (TaskCanceledException)
            {
                StatusChanged?.Invoke("Zeitüberschreitung beim Verbindungstest.");
                return ConnectionTestResult.Unreachable;
            }
        }

        private void AddApiKey(HttpRequestMessage request)
        {
            if (HasApiKey) request.Headers.Add(ApiKeyHeader, _apiKey);
        }

        private static string Describe(Exception ex) =>
            ex.InnerException is { } inner ? $"{ex.Message} ({inner.Message})" : ex.Message;

        /// <summary>
        /// Zertifikate, denen Windows vertraut (z. B. Let's Encrypt), immer annehmen; ein selbst ausgestelltes nur,
        /// wenn sein SHA-256-Fingerabdruck dem aus dem Verbindungscode entspricht.
        /// </summary>
        internal static bool IsCertificateAccepted(X509Certificate? certificate, SslPolicyErrors errors, string? pinnedSha256)
        {
            if (errors == SslPolicyErrors.None) return true;
            if (certificate is null || string.IsNullOrEmpty(pinnedSha256)) return false;
            var actual = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
            return string.Equals(actual, pinnedSha256, StringComparison.OrdinalIgnoreCase);
        }

        private static HttpClient CreateHttpClient(string? pinnedSha256)
        {
            var handler = new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                        IsCertificateAccepted(certificate, errors, pinnedSha256),
                },
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        }

        internal static ConnectionInfo? ParseConnectionCode(string code)
        {
            if (!code.StartsWith(CodePrefix, StringComparison.Ordinal)) return null;
            var base64 = code[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            try
            {
                var info = JsonSerializer.Deserialize<ConnectionInfo>(Convert.FromBase64String(base64));
                return info is { ServerUrl.Length: > 0, ApiKey.Length: > 0 } ? info : null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _retryCts.Cancel();
                _httpClient?.Dispose();
                _flushLock.Dispose();
                _disposed = true;
            }
        }

        private enum SendOutcome { Delivered, Rejected, Retry }

        private sealed record PendingLocation(string CollarId, string CollarName, double Latitude, double Longitude, int? BatteryLevel, DateTimeOffset Timestamp);

        /// <summary>Inhalt des Verbindungscodes (gleiches Format wie LiveTrackingAccess auf dem Server).</summary>
        internal sealed record ConnectionInfo(
            [property: JsonPropertyName("u")] string ServerUrl,
            [property: JsonPropertyName("k")] string ApiKey,
            [property: JsonPropertyName("f")] string? CertificateSha256);
    }
}

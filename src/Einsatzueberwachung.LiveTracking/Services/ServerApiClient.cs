using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Einsatzueberwachung.LiveTracking.Services
{
    public class ServerApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public string ServerUrl { get; private set; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerUrl);

        public event Action<string>? StatusChanged;

        public ServerApiClient()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
            ServerUrl = string.Empty;
        }

        public void Configure(string serverUrl)
        {
            ServerUrl = serverUrl.TrimEnd('/');
            StatusChanged?.Invoke($"Server konfiguriert: {ServerUrl}");
        }

        public void Disconnect()
        {
            ServerUrl = string.Empty;
            StatusChanged?.Invoke("Server-Verbindung getrennt.");
        }

        public async Task<bool> SendCollarLocationAsync(string collarId, string collarName, double latitude, double longitude)
        {
            if (!IsConfigured) return false;

            try
            {
                var payload = new
                {
                    Id = collarId,
                    CollarName = collarName,
                    Coordinates = new { Lat = latitude, Lng = longitude }
                };

                var response = await _httpClient.PostAsJsonAsync(
                    $"{ServerUrl}/api/CollarWebhook/location", payload);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    StatusChanged?.Invoke($"Server-Fehler: {response.StatusCode}");
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                StatusChanged?.Invoke("Server-Timeout");
                return false;
            }
            catch (HttpRequestException ex)
            {
                StatusChanged?.Invoke($"Verbindungsfehler: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (!IsConfigured) return false;

            try
            {
                var response = await _httpClient.GetAsync($"{ServerUrl}/api/CollarWebhook/collars");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}

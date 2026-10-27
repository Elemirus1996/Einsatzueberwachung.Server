// DWD Weather Service - Wetterdaten vom Deutschen Wetterdienst
// Verwendet die BrightSky API (Open Source Proxy fuer DWD Open Data)
// https://brightsky.dev/

using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class DwdWeatherService : IWeatherService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DwdWeatherService>? _logger;
        private readonly ITimeService? _timeService;
        private const string BrightSkyBaseUrl = "https://api.brightsky.dev";
        private const string NominatimBaseUrl = "https://nominatim.openstreetmap.org/search";

        private WeatherData? _cachedWeather;
        private DateTime _cacheTime = DateTime.MinValue;
        private (double lat, double lon) _cachedPosition;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public DwdWeatherService(HttpClient httpClient, ILogger<DwdWeatherService>? logger = null, ITimeService? timeService = null)
        {
            _httpClient = httpClient;
            _logger = logger;
            _timeService = timeService;
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        private DateTime Now => _timeService?.Now ?? Ortszeit.Jetzt;

        public async Task<WeatherData?> GetCurrentWeatherByAddressAsync(string address)
        {
            var coordinates = await GeocodeAddressAsync(address);
            if (coordinates is null)
            {
                return null;
            }

            return await GetCurrentWeatherAsync(coordinates.Value.Latitude, coordinates.Value.Longitude);
        }

        public async Task<(double Latitude, double Longitude)?> GeocodeAddressAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                _logger?.LogWarning("Address-based weather lookup was called with an empty address.");
                return null;
            }

            try
            {
                var encodedAddress = Uri.EscapeDataString(address.Trim());
                var url = $"{NominatimBaseUrl}?q={encodedAddress}&format=jsonv2&limit=1&addressdetails=0";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("Einsatzueberwachung.Server/1.0 (+https://github.com/Elemirus1996/Einsatzueberwachung.Server)");
                request.Headers.Accept.ParseAdd("application/json");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("Nominatim geocoding failed with status {StatusCode} for address '{Address}'", response.StatusCode, address);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                {
                    _logger?.LogWarning("No geocoding result found for address '{Address}'", address);
                    return null;
                }

                var firstResult = doc.RootElement[0];
                if (!firstResult.TryGetProperty("lat", out var latElement) || !firstResult.TryGetProperty("lon", out var lonElement))
                {
                    _logger?.LogWarning("Geocoding response for '{Address}' does not contain coordinates", address);
                    return null;
                }

                var latText = latElement.GetString();
                var lonText = lonElement.GetString();

                if (!double.TryParse(latText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var latitude) ||
                    !double.TryParse(lonText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var longitude))
                {
                    _logger?.LogWarning("Could not parse geocoding coordinates for '{Address}'. lat='{Lat}', lon='{Lon}'", address, latText, lonText);
                    return null;
                }

                _logger?.LogInformation("Address '{Address}' geocoded to lat={Lat}, lon={Lon}", address, latitude, longitude);
                return (latitude, longitude);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception while geocoding address '{Address}'", address);
                return null;
            }
        }

        #region BrightSky API Response Classes

        private class BrightSkyCurrentWeatherResponse
        {
            public BrightSkyWeatherData? Weather { get; set; }
            public BrightSkySource[]? Sources { get; set; }
        }

        private class BrightSkyWeatherResponse
        {
            public BrightSkyWeatherData[]? Weather { get; set; }
            public BrightSkySource[]? Sources { get; set; }
        }

        private class BrightSkyWeatherData
        {
            public DateTime? Timestamp { get; set; }
            public double? Temperature { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("relative_humidity")]
            public double? RelativeHumidity { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("wind_speed_10")]
            public double? WindSpeed10 { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("wind_speed")]
            public double? WindSpeed { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("wind_direction_10")]
            public double? WindDirection10 { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("wind_direction")]
            public double? WindDirection { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("wind_gust_speed_10")]
            public double? WindGustSpeed10 { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("wind_gust_speed")]
            public double? WindGustSpeed { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("precipitation_10")]
            public double? Precipitation10 { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("precipitation")]
            public double? Precipitation { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("cloud_cover")]
            public double? CloudCover { get; set; }

            public double? Visibility { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("pressure_msl")]
            public double? PressureMsl { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("dew_point")]
            public double? DewPoint { get; set; }

            public string? Condition { get; set; }
            public string? Icon { get; set; }

            public double GetWindSpeed() => WindSpeed10 ?? WindSpeed ?? 0;
            public double GetWindDirection() => WindDirection10 ?? WindDirection ?? 0;
            public double GetWindGustSpeed() => WindGustSpeed10 ?? WindGustSpeed ?? 0;
            public double GetPrecipitation() => Precipitation10 ?? Precipitation ?? 0;
        }

        private class BrightSkySource
        {
            public int? Id { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("station_name")]
            public string? StationName { get; set; }
        }

        #endregion
    }
}

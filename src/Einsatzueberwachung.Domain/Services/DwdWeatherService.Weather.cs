using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class DwdWeatherService
    {
        public async Task<WeatherData?> GetCurrentWeatherAsync(double latitude, double longitude)
        {
            if (_cachedWeather != null &&
                Now - _cacheTime < CacheDuration &&
                Math.Abs(_cachedPosition.lat - latitude) < 0.01 &&
                Math.Abs(_cachedPosition.lon - longitude) < 0.01)
            {
                return _cachedWeather;
            }

            try
            {
                var url = $"{BrightSkyBaseUrl}/current_weather?lat={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

                _logger?.LogInformation("Fetching weather from: {Url}", url);

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("API Error: {StatusCode}", response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                _logger?.LogDebug("API Response: {Json}", json.Substring(0, Math.Min(300, json.Length)));

                var weatherResponse = JsonSerializer.Deserialize<BrightSkyCurrentWeatherResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (weatherResponse?.Weather == null)
                {
                    _logger?.LogWarning("Weather data is null after deserialization. JSON: {Json}", json.Substring(0, Math.Min(200, json.Length)));
                    return null;
                }

                _logger?.LogInformation("Parsed weather: Temp={Temp}C, Wind={Wind}km/h, Condition={Condition}",
                    weatherResponse.Weather.Temperature,
                    weatherResponse.Weather.GetWindSpeed(),
                    weatherResponse.Weather.Condition);

                var weather = MapToWeatherData(weatherResponse.Weather);

                _cachedWeather = weather;
                _cacheTime = Now;
                _cachedPosition = (latitude, longitude);

                return weather;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception while fetching weather data");
                return null;
            }
        }

        public async Task<WeatherForecast?> GetForecastAsync(double latitude, double longitude)
        {
            try
            {
                var now = DateTime.UtcNow;
                var url = $"{BrightSkyBaseUrl}/weather?lat={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&date={now:yyyy-MM-dd}";

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var weatherResponse = JsonSerializer.Deserialize<BrightSkyWeatherResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (weatherResponse?.Weather == null || weatherResponse.Weather.Length == 0)
                    return null;

                var forecast = new WeatherForecast
                {
                    LetzteAktualisierung = Now,
                    StundenVorhersage = new WeatherData[Math.Min(24, weatherResponse.Weather.Length)]
                };

                for (int i = 0; i < forecast.StundenVorhersage.Length && i < weatherResponse.Weather.Length; i++)
                {
                    forecast.StundenVorhersage[i] = MapToWeatherData(weatherResponse.Weather[i]);
                }

                return forecast;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Fehler beim Abrufen der Wettervorhersage");
                return null;
            }
        }

        private WeatherData MapToWeatherData(BrightSkyWeatherData data)
        {
            var windSpeed = data.GetWindSpeed();
            var windDir = data.GetWindDirection();

            var weather = new WeatherData
            {
                Zeitpunkt = data.Timestamp ?? Now,
                Temperatur = data.Temperature ?? 0,
                Luftfeuchtigkeit = (int)(data.RelativeHumidity ?? 0),
                Windgeschwindigkeit = windSpeed,
                Windboeen = data.GetWindGustSpeed(),
                Windrichtung = (int)windDir,
                WindrichtungText = GetWindrichtungText((int)windDir),
                Niederschlag = data.GetPrecipitation(),
                Bewoelkung = (int)(data.CloudCover ?? 0),
                Sichtweite = (data.Visibility ?? 10000) / 1000.0,
                Luftdruck = data.PressureMsl ?? 1013,
                Taupunkt = data.DewPoint ?? 0,
                Wetterlage = MapConditionToGerman(data.Condition),
                IstTag = data.Timestamp?.Hour >= 6 && data.Timestamp?.Hour < 20
            };

            weather.GefuehlteTemperatur = CalculateFeelsLike(
                weather.Temperatur,
                weather.Windgeschwindigkeit,
                weather.Luftfeuchtigkeit);

            return weather;
        }

        private string GetWindrichtungText(int degrees)
        {
            if (degrees < 0) return "";

            var directions = new[] { "N", "NNO", "NO", "ONO", "O", "OSO", "SO", "SSO",
                                     "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
            var index = (int)Math.Round(degrees / 22.5) % 16;
            return directions[index];
        }

        private string MapConditionToGerman(string? condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return "Unbekannt";

            return condition.ToLowerInvariant() switch
            {
                "dry" => "Trocken",
                "fog" => "Nebel",
                "rain" => "Regen",
                "sleet" => "Schneeregen",
                "snow" => "Schnee",
                "hail" => "Hagel",
                "thunderstorm" => "Gewitter",
                "clear-day" => "Sonnig",
                "clear-night" => "Klar",
                "partly-cloudy-day" => "Teilweise bewölkt",
                "partly-cloudy-night" => "Teilweise bewölkt",
                "cloudy" => "Bewölkt",
                "wind" => "Windig",
                _ => condition
            };
        }

        private double CalculateFeelsLike(double temp, double windSpeed, int humidity)
        {
            if (temp <= 10 && windSpeed > 4.8)
            {
                var windChill = 13.12 + 0.6215 * temp - 11.37 * Math.Pow(windSpeed, 0.16) + 0.3965 * temp * Math.Pow(windSpeed, 0.16);
                return Math.Round(windChill, 1);
            }

            if (temp >= 27 && humidity >= 40)
            {
                var hi = -8.784695 + 1.61139411 * temp + 2.338549 * humidity
                         - 0.14611605 * temp * humidity - 0.012308094 * temp * temp
                         - 0.016424828 * humidity * humidity;
                return Math.Round(hi, 1);
            }

            return temp;
        }
    }
}

// Implementierung des Settings-Service
// Quelle: Abgeleitet von WPF Services/SettingsService.cs

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly string _dataPath;
        private StaffelSettings? _staffelSettings;
        private AppSettings? _appSettings;

        public SettingsService()
        {
            _dataPath = AppPathResolver.GetDataDirectory();
        }

        public async Task<StaffelSettings> GetStaffelSettingsAsync()
        {
            if (_staffelSettings != null)
                return _staffelSettings;

            var filePath = Path.Combine(_dataPath, "StaffelSettings.json");
            
            if (File.Exists(filePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    _staffelSettings = JsonSerializer.Deserialize<StaffelSettings>(json) ?? new StaffelSettings();
                }
                catch
                {
                    _staffelSettings = new StaffelSettings();
                }
            }
            else
            {
                _staffelSettings = new StaffelSettings();
            }

            return _staffelSettings;
        }

        public async Task SaveStaffelSettingsAsync(StaffelSettings settings)
        {
            _staffelSettings = settings;
            var filePath = Path.Combine(_dataPath, "StaffelSettings.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<AppSettings> GetAppSettingsAsync()
        {
            if (_appSettings != null)
                return _appSettings;

            var filePath = Path.Combine(_dataPath, "AppSettings.json");
            
            if (File.Exists(filePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    _appSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch
                {
                    _appSettings = new AppSettings();
                }
            }
            else
            {
                _appSettings = new AppSettings();
            }

            return _appSettings;
        }

        public async Task SaveAppSettingsAsync(AppSettings settings)
        {
            AppDatenschutz.Aktualisieren(settings, DateTime.UtcNow);
            _appSettings = settings;
            var filePath = Path.Combine(_dataPath, "AppSettings.json");
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<bool> GetIsDarkModeAsync()
        {
            var settings = await GetAppSettingsAsync();
            return settings.IsDarkMode;
        }

        public async Task SetIsDarkModeAsync(bool isDark)
        {
            var settings = await GetAppSettingsAsync();
            settings.IsDarkMode = isDark;
            settings.Theme = isDark ? "Dark" : "Light";
            await SaveAppSettingsAsync(settings);
        }
    }
}

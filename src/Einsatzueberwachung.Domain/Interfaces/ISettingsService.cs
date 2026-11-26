// Service-Interface für Einstellungen/Settings-Verwaltung
// Quelle: Abgeleitet von WPF ViewModels/SettingsViewModel.cs und Services/SettingsService.cs

using System;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Interfaces
{
    public interface ISettingsService
    {
        event Action? AppSettingsChanged;

        Task<StaffelSettings> GetStaffelSettingsAsync();
        Task SaveStaffelSettingsAsync(StaffelSettings settings);

        Task<AppSettings> GetAppSettingsAsync();
        Task SaveAppSettingsAsync(AppSettings settings);

        Task<bool> GetIsDarkModeAsync();
        Task SetIsDarkModeAsync(bool isDark);
    }
}

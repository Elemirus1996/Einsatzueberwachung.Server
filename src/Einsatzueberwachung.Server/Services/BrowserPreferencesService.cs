using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Scoped-Service: verwaltet Browser-Preferences.
/// Theme-Einstellungen kommen serverseitig aus AppSettings und gelten global.
/// Browser-lokale Werte (z.B. Sound/Shortcuts) bleiben im localStorage.
/// </summary>
public sealed class BrowserPreferencesService
{
    private const string LocalStorageKey = "browser-prefs";

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly IJSRuntime _js;
    private readonly ISettingsService _settingsService;

    private BrowserPreferences _prefs = new();
    private bool _loaded;

    public BrowserPreferencesService(IJSRuntime js, ISettingsService settingsService)
    {
        _js = js;
        _settingsService = settingsService;
    }

    /// <summary>Die aktuell geladenen Browser-Einstellungen.</summary>
    public BrowserPreferences Preferences => _prefs;

    /// <summary>
    /// Lädt Einstellungen aus localStorage. Beim ersten Aufruf (noch kein Eintrag)
    /// werden die bisherigen AppSettings als Migrations-Fallback genutzt.
    /// Wirft keine Exception — bei JS-Fehler (Prerendering) bleibt _loaded = false
    /// damit der nächste Aufruf es erneut versucht.
    /// </summary>
    public async Task LoadAsync()
    {
        if (_loaded) return;

        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", LocalStorageKey);

            if (!string.IsNullOrWhiteSpace(json))
            {
                _prefs = JsonSerializer.Deserialize<BrowserPreferences>(json, JsonOpts)
                         ?? new BrowserPreferences();
            }
            else
            {
                // Erster Aufruf: Migration aus bestehenden AppSettings
                await MigrateFromAppSettingsAsync();
            }

            await ApplyServerThemeAsync();
            NormalizeThemeValues();
            _loaded = true;
        }
        catch
        {
            // JS noch nicht verfügbar (Prerendering) → _loaded bleibt false
        }
    }

    /// <summary>Speichert die aktuellen Preferences in localStorage.</summary>
    public async Task SaveAsync()
    {
        _loaded = true;
        var json = JsonSerializer.Serialize(_prefs, JsonOpts);
        await _js.InvokeVoidAsync("localStorage.setItem", LocalStorageKey, json);
    }

    /// <summary>Speichert nur Theme-relevante Werte zentral in AppSettings.</summary>
    public async Task SaveThemeToServerAsync()
    {
        NormalizeThemeValues();

        var app = await _settingsService.GetAppSettingsAsync();
        app.ThemeMode = _prefs.ThemeMode;
        app.IsDarkMode = _prefs.IsDarkMode;

        if (TimeSpan.TryParse(_prefs.DarkModeStartTime, out var start))
        {
            app.DarkModeStartTime = start;
        }

        if (TimeSpan.TryParse(_prefs.DarkModeEndTime, out var end))
        {
            app.DarkModeEndTime = end;
        }

        app.ThemePreset = _prefs.ThemePreset;
        app.VisualIntensity = _prefs.VisualIntensity;
        app.CustomThemes = _prefs.CustomThemes?.ToList() ?? new List<CustomTheme>();

        await _settingsService.SaveAppSettingsAsync(app);
    }

    /// <summary>Ändert Felder der Preferences ohne sofort zu speichern.</summary>
    public void Update(Action<BrowserPreferences> mutate) => mutate(_prefs);

    /// <summary>Setzt Preferences auf Standardwerte zurück und speichert.</summary>
    public async Task ResetAsync()
    {
        _prefs = new BrowserPreferences();
        await SaveAsync();
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private async Task MigrateFromAppSettingsAsync()
    {
        try
        {
            var app = await _settingsService.GetAppSettingsAsync();
            _prefs = new BrowserPreferences
            {
                ThemeMode              = app.ThemeMode ?? "Manual",
                IsDarkMode             = app.IsDarkMode,
                DarkModeStartTime      = app.DarkModeStartTime.ToString(@"hh\:mm"),
                DarkModeEndTime        = app.DarkModeEndTime.ToString(@"hh\:mm"),
                ThemePreset            = string.IsNullOrWhiteSpace(app.ThemePreset) ? ThemePresets.Nrw : app.ThemePreset,
                VisualIntensity        = string.IsNullOrWhiteSpace(app.VisualIntensity) ? VisualIntensityLevels.Ausgewogen : app.VisualIntensity,
                CustomThemes           = app.CustomThemes?.ToList() ?? new List<CustomTheme>(),
                SoundAlertsEnabled     = app.SoundAlertsEnabled,
                SoundVolume            = app.SoundVolume > 0 ? app.SoundVolume : 70,
                FirstWarningSound      = string.IsNullOrWhiteSpace(app.FirstWarningSound) ? "beep" : app.FirstWarningSound,
                SecondWarningSound     = string.IsNullOrWhiteSpace(app.SecondWarningSound) ? "alarm" : app.SecondWarningSound,
                FirstWarningFrequency  = app.FirstWarningFrequency > 0 ? app.FirstWarningFrequency : 800,
                SecondWarningFrequency = app.SecondWarningFrequency > 0 ? app.SecondWarningFrequency : 1200,
                RepeatSecondWarning    = app.RepeatSecondWarning,
                RepeatWarningIntervalSeconds = app.RepeatWarningIntervalSeconds > 0
                                             ? app.RepeatWarningIntervalSeconds : 30,
            };

            NormalizeThemeValues();
        }
        catch
        {
            _prefs = new BrowserPreferences();
        }
    }

    private async Task ApplyServerThemeAsync()
    {
        var app = await _settingsService.GetAppSettingsAsync();

        _prefs.ThemeMode = string.IsNullOrWhiteSpace(app.ThemeMode) ? "Manual" : app.ThemeMode;
        _prefs.IsDarkMode = app.IsDarkMode;
        _prefs.DarkModeStartTime = app.DarkModeStartTime.ToString(@"hh\:mm");
        _prefs.DarkModeEndTime = app.DarkModeEndTime.ToString(@"hh\:mm");
        _prefs.ThemePreset = string.IsNullOrWhiteSpace(app.ThemePreset) ? ThemePresets.Nrw : app.ThemePreset;
        _prefs.VisualIntensity = string.IsNullOrWhiteSpace(app.VisualIntensity) ? VisualIntensityLevels.Ausgewogen : app.VisualIntensity;
        _prefs.CustomThemes = app.CustomThemes?.ToList() ?? new List<CustomTheme>();
    }

    private void NormalizeThemeValues()
    {
        _prefs.ThemePreset = NormalizePreset(_prefs.ThemePreset);
        _prefs.VisualIntensity = NormalizeIntensity(_prefs.VisualIntensity);
    }

    private static string NormalizePreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            return ThemePresets.Nrw;
        }

        if (preset.Equals(ThemePresets.Ruhr, StringComparison.OrdinalIgnoreCase))
        {
            return ThemePresets.Ruhr;
        }

        if (preset.Equals(ThemePresets.Nrw, StringComparison.OrdinalIgnoreCase))
        {
            return ThemePresets.Nrw;
        }

        // Benutzerdefinierte Themes werden über ihre gespeicherte Theme-ID geladen.
        return preset.Trim();
    }

    private static string NormalizeIntensity(string? intensity)
    {
        if (string.IsNullOrWhiteSpace(intensity))
        {
            return VisualIntensityLevels.Ausgewogen;
        }

        if (intensity.Equals(VisualIntensityLevels.Dezent, StringComparison.OrdinalIgnoreCase))
        {
            return VisualIntensityLevels.Dezent;
        }

        if (intensity.Equals(VisualIntensityLevels.Lebhaft, StringComparison.OrdinalIgnoreCase))
        {
            return VisualIntensityLevels.Lebhaft;
        }

        return VisualIntensityLevels.Ausgewogen;
    }
}

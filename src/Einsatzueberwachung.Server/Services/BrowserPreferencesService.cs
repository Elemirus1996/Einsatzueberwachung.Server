using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Scoped-Service: speichert Theme- und Sound-Einstellungen im localStorage
/// des jeweiligen Browsers. Jeder Browser-Tab hat eine eigene Instanz und
/// damit eigene Darstellungseinstellungen — die gemeinsamen Einsatzdaten
/// (Singleton-Services) sind davon nicht betroffen.
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

            // Mark loaded only after JS succeeded
            _loaded = true;

            if (!string.IsNullOrWhiteSpace(json))
            {
                _prefs = JsonSerializer.Deserialize<BrowserPreferences>(json, JsonOpts)
                         ?? new BrowserPreferences();
                return;
            }

            // Erster Aufruf: Migration aus bestehenden AppSettings
            await MigrateFromAppSettingsAsync();
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
        }
        catch
        {
            _prefs = new BrowserPreferences();
        }
    }
}

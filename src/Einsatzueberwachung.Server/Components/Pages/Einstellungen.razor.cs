using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class Einstellungen
{
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private IMasterDataService MasterDataService { get; set; } = default!;
    [Inject] private IArchivService ArchivService { get; set; } = default!;
    [Inject] private GitHubUpdateService UpdateService { get; set; } = default!;
    [Inject] private BrowserPreferencesService BrowserPrefs { get; set; } = default!;
    [Inject] private ITimeService TimeService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IDiveraService DiveraService { get; set; } = default!;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private string _appVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    private StaffelSettings _staffelSettings = new();
    private AppSettings _appSettings = new();
    private string _status = string.Empty;
    private string _currentServerTime = string.Empty;
    private string _dataDirectory = string.Empty;
    private string _reportDirectory = string.Empty;
    private string _mobileUrl = string.Empty;
    private string _sessionImportJson = string.Empty;
    private string _maintenanceStatus = string.Empty;
    private bool _maintenanceError;
    private string _logoStatus = string.Empty;
    private bool _logoStatusIsError;
    private string _pdfLogoStatus = string.Empty;
    private bool _pdfLogoStatusIsError;
    private string _audioTestStatus = string.Empty;
    private bool _audioTestError;

    // --- Tastenkürzel ---
    private string? _recordingField;
    private KeyboardShortcutPreferences _shortcuts = new();
    private UpdateRuntimeStatus _updateStatus = new();
    private string _quickNoteTemplatesText = string.Empty;
    private List<string> _quickNoteList = new();

    // Divera 24/7 Felder
    private bool _showDiveraKey;
    private bool _diveraTestBusy;
    private string _diveraTestStatus = string.Empty;
    private bool _diveraTestError;

    // Aktenregister-State
    [SupplyParameterFromQuery(Name = "tab")] private string? TabFromQuery { get; set; }
    private SettingsTab _activeTab = SettingsTab.Staffel;
    private bool _showSavedToast;

    private enum SettingsTab
    {
        Staffel,
        Einsatzbetrieb,
        Erscheinungsbild,
        Tastenkuerzel,
        Integration,
        System,
        Wartung
    }

    // ── Status-Badge-Helpers ───────────────────────────────────────────

    private bool StaffelComplete =>
        !string.IsNullOrWhiteSpace(_staffelSettings.StaffelName)
        && !string.IsNullOrWhiteSpace(_staffelSettings.StaffelAdresse)
        && !string.IsNullOrWhiteSpace(_staffelSettings.StaffelTelefon)
        && !string.IsNullOrWhiteSpace(_staffelSettings.StaffelEmail);

    private int IntegrationActiveCount =>
        (_appSettings.DiveraEnabled && !string.IsNullOrWhiteSpace(_appSettings.DiveraAccessKey) ? 1 : 0)
        + 1; // GPS-Livetracking immer "verfügbar" (Download-Link), zählt als 1

    private bool UpdateAvailable => _updateStatus.UpdateAvailable;

    private int? BackupAgeDays
    {
        get
        {
            if (_appSettings.LastBackupAt is null) return null;
            return (int)Math.Floor((TimeService.Now - _appSettings.LastBackupAt.Value).TotalDays);
        }
    }

    private string BackupAgeLabel
    {
        get
        {
            var d = BackupAgeDays;
            if (d is null) return "nie";
            if (d == 0) return "heute";
            if (d == 1) return "gestern";
            return $"vor {d}T";
        }
    }

    private string BackupAgeSeverity
    {
        get
        {
            var d = BackupAgeDays;
            if (d is null) return "danger";
            if (d <= 7) return "ok";
            if (d <= 30) return "warning";
            return "danger";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _staffelSettings = await SettingsService.GetStaffelSettingsAsync();
        _appSettings = await SettingsService.GetAppSettingsAsync();
        EnsureQuickNoteTemplatesInitialized(_appSettings);
        _quickNoteTemplatesText = string.Join(Environment.NewLine, _appSettings.QuickNoteTemplates);
        _quickNoteList = new List<string>(_appSettings.QuickNoteTemplates);
        _currentServerTime = TimeService.Now.ToString("dd.MM.yyyy HH:mm:ss");
        _dataDirectory = AppPathResolver.GetDataDirectory();
        _reportDirectory = AppPathResolver.GetReportDirectory();
        _mobileUrl = new Uri(new Uri(Navigation.BaseUri), "mobile/").ToString();
        _updateStatus = UpdateService.GetStatusSnapshot();

        ApplyTabFromQuery();
    }

    protected override void OnParametersSet()
    {
        ApplyTabFromQuery();
    }

    private void ApplyTabFromQuery()
    {
        if (string.IsNullOrWhiteSpace(TabFromQuery)) return;
        if (Enum.TryParse<SettingsTab>(TabFromQuery, ignoreCase: true, out var parsed))
            _activeTab = parsed;
    }

    private void SetActiveTab(SettingsTab tab)
    {
        _activeTab = tab;
        var url = Navigation.GetUriWithQueryParameter("tab", tab.ToString().ToLowerInvariant());
        Navigation.NavigateTo(url, forceLoad: false, replace: true);
    }

    private async Task MarkBackupCreatedAsync()
    {
        _appSettings.LastBackupAt = TimeService.Now;
        await SettingsService.SaveAppSettingsAsync(_appSettings);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        // Zertifikat abfragen und VPN-Status lesen kann einige Sekunden dauern – nicht beim ersten Rendern blockieren
        _ = LoadLiveTrackingCodeAsync();
        _ = LoadVpnAsync();
        // BrowserPrefs aus localStorage laden (vom MainLayout ggf. bereits geladen)
        await BrowserPrefs.LoadAsync();
        _shortcuts = System.Text.Json.JsonSerializer.Deserialize<KeyboardShortcutPreferences>(
            System.Text.Json.JsonSerializer.Serialize(BrowserPrefs.Preferences.Shortcuts))
            ?? new KeyboardShortcutPreferences();
        StateHasChanged();
    }

    private async Task CheckForUpdatesAsync()
    {
        _updateStatus = UpdateService.GetStatusSnapshot();
        await InvokeAsync(StateHasChanged);

        try
        {
            await UpdateService.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            _updateStatus.LastError = ex.Message;
            _updateStatus.LastMessage = "Update-Prüfung fehlgeschlagen";
        }
        finally
        {
            _updateStatus = UpdateService.GetStatusSnapshot();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task InstallUpdateAsync()
    {
        _updateStatus = UpdateService.GetStatusSnapshot();
        await InvokeAsync(StateHasChanged);

        try
        {
            await UpdateService.InstallLatestAsync();
        }
        catch (Exception ex)
        {
            _updateStatus.LastError = ex.Message;
            _updateStatus.LastMessage = "Update fehlgeschlagen";
        }
        finally
        {
            _updateStatus = UpdateService.GetStatusSnapshot();
            await InvokeAsync(StateHasChanged);
        }
    }

    private string DisplayLatestVersion()
    {
        return string.IsNullOrWhiteSpace(_updateStatus.LatestVersion) ? "-" : _updateStatus.LatestVersion;
    }

    private static string DisplayDateTime(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("dd.MM.yyyy HH:mm") : "-";
    }

    private async Task SaveAsync()
    {
        // --- Server-globale Einstellungen (Timer, Updates, Schnell-Notizen) ---
        _appSettings.QuickNoteTemplates = _quickNoteList
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_appSettings.QuickNoteTemplates.Count == 0)
        {
            _appSettings.QuickNoteTemplates = GetDefaultQuickNoteTemplates();
            _quickNoteList = new List<string>(_appSettings.QuickNoteTemplates);
        }
        _quickNoteTemplatesText = string.Join(Environment.NewLine, _appSettings.QuickNoteTemplates);

        await SettingsService.SaveStaffelSettingsAsync(_staffelSettings);
        await SettingsService.SaveAppSettingsAsync(_appSettings);

        // Divera-Konfiguration sofort uebernehmen
        await DiveraService.RefreshConfigurationAsync();

        // Zeitzone sofort neu laden, damit neue Zeitstempel korrekt sind
        TimeService.Refresh();
        _currentServerTime = TimeService.Now.ToString("dd.MM.yyyy HH:mm:ss");

        // --- Browser-lokale Einstellungen (Theme + Sound) ---
        var prefs = BrowserPrefs.Preferences;
        prefs.ThemePreset = NormalizeThemePreset(prefs.ThemePreset);
        prefs.VisualIntensity = NormalizeVisualIntensity(prefs.VisualIntensity);

        // Scheduled-Modus: Dark/Light sofort berechnen
        if (prefs.ThemeMode == "Scheduled")
        {
            if (TimeSpan.TryParse(prefs.DarkModeStartTime, out var start)
                && TimeSpan.TryParse(prefs.DarkModeEndTime, out var end))
            {
                var now = Ortszeit.Jetzt.TimeOfDay;
                prefs.IsDarkMode = start < end
                    ? now >= start && now < end
                    : now >= start || now < end;
            }
        }

        await BrowserPrefs.SaveAsync();
        await BrowserPrefs.SaveThemeToServerAsync();

        // Theme im Browser sofort anwenden
        if (prefs.ThemeMode == "Auto")
        {
            await ApplyThemeStateAsync(prefs.IsDarkMode);
            await JS.InvokeVoidAsync("themeSync.watchSystemTheme");
        }
        else
        {
            await JS.InvokeVoidAsync("themeSync.stopWatchingSystemTheme");
            await ApplyThemeStateAsync(prefs.IsDarkMode);
        }

        _status = "Einstellungen gespeichert.";
        _showSavedToast = true;
        SetLogoStatus(string.Empty, false);
        SetPdfLogoStatus(string.Empty, false);
        _ = HideToastAfterDelayAsync();
    }

    private async Task HideToastAfterDelayAsync()
    {
        await Task.Delay(2400);
        _showSavedToast = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task UploadStaffelLogoAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file is null)
        {
            SetLogoStatus("Keine Datei ausgewählt.", true);
            return;
        }

        var extension = Path.GetExtension(file.Name)?.ToLowerInvariant() ?? string.Empty;
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".svg"
        };

        if (!allowed.Contains(extension))
        {
            SetLogoStatus("Ungültiges Format. Erlaubt sind PNG, JPG, JPEG, WEBP oder SVG.", true);
            return;
        }

        try
        {
            var logoDirectory = Path.Combine(AppPathResolver.GetDataDirectory(), "logos");
            Directory.CreateDirectory(logoDirectory);

            var targetPath = Path.Combine(logoDirectory, $"staffel-logo{extension}");
            await using var source = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
            await using var target = File.Create(targetPath);
            await source.CopyToAsync(target);

            _staffelSettings.StaffelLogoPfad = targetPath;
            await SettingsService.SaveStaffelSettingsAsync(_staffelSettings);

            SetLogoStatus("Logo wurde hochgeladen und gespeichert.", false);
            _status = "Einstellungen gespeichert.";
        }
        catch (Exception ex)
        {
            SetLogoStatus($"Logo-Upload fehlgeschlagen: {ex.Message}", true);
        }
    }

    private async Task ClearStaffelLogoAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_staffelSettings.StaffelLogoPfad) && File.Exists(_staffelSettings.StaffelLogoPfad))
            {
                File.Delete(_staffelSettings.StaffelLogoPfad);
            }

            _staffelSettings.StaffelLogoPfad = string.Empty;
            await SettingsService.SaveStaffelSettingsAsync(_staffelSettings);
            SetLogoStatus("Logo wurde entfernt.", false);
        }
        catch (Exception ex)
        {
            SetLogoStatus($"Logo konnte nicht entfernt werden: {ex.Message}", true);
        }
    }


    private bool _isEditingCustomTheme;
    private Einsatzueberwachung.Domain.Models.CustomTheme? _editingCustomTheme;

    private void StartNewCustomTheme()
    {
        _editingCustomTheme = new Einsatzueberwachung.Domain.Models.CustomTheme
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Neues Theme",
            PrimaryColor = "#005D9E",
            SecondaryColor = "#FFED00",
            TertiaryColor = "#28A745",
            QuaternaryColor = "#FFFFFF"
        };
        _isEditingCustomTheme = true;
    }

    private void EditCustomTheme(Einsatzueberwachung.Domain.Models.CustomTheme theme)
    {
        _editingCustomTheme = new Einsatzueberwachung.Domain.Models.CustomTheme
        {
            Id = theme.Id,
            Name = theme.Name,
            PrimaryColor = theme.PrimaryColor,
            SecondaryColor = theme.SecondaryColor,
            TertiaryColor = theme.TertiaryColor,
            QuaternaryColor = theme.QuaternaryColor
        };
        _isEditingCustomTheme = true;
    }

    private void CancelEditCustomTheme()
    {
        _editingCustomTheme = null;
        _isEditingCustomTheme = false;
    }

    private async Task SaveCustomThemeAsync()
    {
        if (_editingCustomTheme != null)
        {
            if (BrowserPrefs.Preferences.CustomThemes == null)
            {
                BrowserPrefs.Preferences.CustomThemes = new List<Einsatzueberwachung.Domain.Models.CustomTheme>();
            }

            var existing = BrowserPrefs.Preferences.CustomThemes.FirstOrDefault(t => t.Id == _editingCustomTheme.Id);
            if (existing != null)
            {
                existing.Name = _editingCustomTheme.Name;
                existing.PrimaryColor = _editingCustomTheme.PrimaryColor;
                existing.SecondaryColor = _editingCustomTheme.SecondaryColor;
                existing.TertiaryColor = _editingCustomTheme.TertiaryColor;
                existing.QuaternaryColor = _editingCustomTheme.QuaternaryColor;
            }
            else
            {
                BrowserPrefs.Preferences.CustomThemes.Add(_editingCustomTheme);
            }

            if (BrowserPrefs.Preferences.ThemePreset == _editingCustomTheme.Id)
            {
                await SetThemePresetAsync(new ChangeEventArgs { Value = _editingCustomTheme.Id });
            }
            else
            {
                await BrowserPrefs.SaveAsync();
                await BrowserPrefs.SaveThemeToServerAsync();
            }

            _editingCustomTheme = null;
            _isEditingCustomTheme = false;
        }
    }

    private async Task DeleteCustomThemeAsync(string id)
    {
        if (BrowserPrefs.Preferences.CustomThemes != null)
        {
            var theme = BrowserPrefs.Preferences.CustomThemes.FirstOrDefault(t => t.Id == id);
            if (theme != null)
            {
                BrowserPrefs.Preferences.CustomThemes.Remove(theme);

                if (BrowserPrefs.Preferences.ThemePreset == id)
                {
                    await SetThemePresetAsync(new ChangeEventArgs { Value = "NRW" });
                }
                else
                {
                    await BrowserPrefs.SaveAsync();
                    await BrowserPrefs.SaveThemeToServerAsync();
                }
            }
        }
    }

private async Task SetDesignModeAsync(bool darkMode)
    {
        if (BrowserPrefs.Preferences.ThemeMode != "Manual")
            return;

        BrowserPrefs.Update(p => p.IsDarkMode = darkMode);
        await BrowserPrefs.SaveAsync();
        await BrowserPrefs.SaveThemeToServerAsync();
        await ApplyThemeStateAsync(darkMode);
    }

    private async Task SetThemePresetAsync(ChangeEventArgs e)
    {
        var selected = e.Value?.ToString();
        BrowserPrefs.Update(p => p.ThemePreset = NormalizeThemePreset(selected));
        await BrowserPrefs.SaveAsync();
        await BrowserPrefs.SaveThemeToServerAsync();
        await ApplyThemeStateAsync(BrowserPrefs.Preferences.IsDarkMode);
    }

    private async Task SetVisualIntensityAsync(ChangeEventArgs e)
    {
        var selected = e.Value?.ToString();
        BrowserPrefs.Update(p => p.VisualIntensity = NormalizeVisualIntensity(selected));
        await BrowserPrefs.SaveAsync();
        await BrowserPrefs.SaveThemeToServerAsync();
        await ApplyThemeStateAsync(BrowserPrefs.Preferences.IsDarkMode);
    }

    private async Task ApplyThemeStateAsync(bool isDark)
    {
        await JS.InvokeVoidAsync("themeSync.setThemeState", new
        {
            isDark,
            preset = BrowserPrefs.Preferences.ThemePreset,
            intensity = BrowserPrefs.Preferences.VisualIntensity
        });
    }

    private static string NormalizeThemePreset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ThemePresets.Nrw;
        }

        if (string.Equals(value, ThemePresets.Ruhr, StringComparison.OrdinalIgnoreCase))
        {
            return ThemePresets.Ruhr;
        }

        if (string.Equals(value, ThemePresets.Nrw, StringComparison.OrdinalIgnoreCase))
        {
            return ThemePresets.Nrw;
        }

        // Benutzerdefinierte Themes speichern ihre eigene ID als Preset-Wert.
        return value.Trim();
    }

    private static string NormalizeVisualIntensity(string? value)
    {
        if (string.Equals(value, VisualIntensityLevels.Dezent, StringComparison.OrdinalIgnoreCase))
        {
            return VisualIntensityLevels.Dezent;
        }

        if (string.Equals(value, VisualIntensityLevels.Lebhaft, StringComparison.OrdinalIgnoreCase))
        {
            return VisualIntensityLevels.Lebhaft;
        }

        return VisualIntensityLevels.Ausgewogen;
    }

    private async Task ResetDataAsync()
    {
        await MasterDataService.SaveSessionDataAsync(new SessionData());
        await SettingsService.SaveStaffelSettingsAsync(new StaffelSettings());
        await SettingsService.SaveAppSettingsAsync(new AppSettings());

        _staffelSettings = await SettingsService.GetStaffelSettingsAsync();
        _appSettings = await SettingsService.GetAppSettingsAsync();
        EnsureQuickNoteTemplatesInitialized(_appSettings);
        _quickNoteTemplatesText = string.Join(Environment.NewLine, _appSettings.QuickNoteTemplates);
        _quickNoteList = new List<string>(_appSettings.QuickNoteTemplates);
        _sessionImportJson = string.Empty;
        SetMaintenanceStatus("Daten wurden auf Standard zurückgesetzt.", false);
    }

    private async Task ConfirmResetDataAsync()
    {
        var confirmed = await JS.InvokeAsync<bool>("confirm", "Sollen die Einsatzdaten und Einstellungen wirklich auf Standard zurückgesetzt werden?");
        if (!confirmed)
        {
            return;
        }

        await ResetDataAsync();
    }

    private async Task ImportSessionJsonAsync()
    {
        if (string.IsNullOrWhiteSpace(_sessionImportJson))
        {
            SetMaintenanceStatus("Bitte zuerst SessionData-JSON einfügen.", true);
            return;
        }

        try
        {
            var imported = JsonSerializer.Deserialize<SessionData>(_sessionImportJson, _jsonOptions);
            if (imported is null)
            {
                SetMaintenanceStatus("Import fehlgeschlagen: JSON konnte nicht gelesen werden.", true);
                return;
            }

            await MasterDataService.SaveSessionDataAsync(imported);
            SetMaintenanceStatus("SessionData wurde importiert.", false);
        }
        catch (Exception ex)
        {
            SetMaintenanceStatus($"Import fehlgeschlagen: {ex.Message}", true);
        }
    }

    private async Task ImportSessionFileAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file is null)
        {
            SetMaintenanceStatus("Keine Datei ausgewählt.", true);
            return;
        }

        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
            using var reader = new StreamReader(stream);
            _sessionImportJson = await reader.ReadToEndAsync();
            await ImportSessionJsonAsync();
        }
        catch (Exception ex)
        {
            SetMaintenanceStatus($"Datei-Import fehlgeschlagen: {ex.Message}", true);
        }
    }

    private async Task ImportAppSettingsFileAsync(InputFileChangeEventArgs args)
    {
        try
        {
            var imported = await ReadJsonFromInputFileAsync<AppSettings>(args, 2 * 1024 * 1024);
            if (imported is null)
            {
                SetMaintenanceStatus("AppSettings-Import fehlgeschlagen: Datei war leer oder ungültig.", true);
                return;
            }

            SettingsSecrets.KeepExistingSecrets(imported, await SettingsService.GetAppSettingsAsync());
            await SettingsService.SaveAppSettingsAsync(imported);
            _appSettings = await SettingsService.GetAppSettingsAsync();
            EnsureQuickNoteTemplatesInitialized(_appSettings);
            _quickNoteTemplatesText = string.Join(Environment.NewLine, _appSettings.QuickNoteTemplates);
            _quickNoteList = new List<string>(_appSettings.QuickNoteTemplates);
            SetMaintenanceStatus("AppSettings wurden importiert.", false);
        }
        catch (Exception ex)
        {
            SetMaintenanceStatus($"AppSettings-Import fehlgeschlagen: {ex.Message}", true);
        }
    }

    private async Task ImportStaffelSettingsFileAsync(InputFileChangeEventArgs args)
    {
        try
        {
            var imported = await ReadJsonFromInputFileAsync<StaffelSettings>(args, 2 * 1024 * 1024);
            if (imported is null)
            {
                SetMaintenanceStatus("StaffelSettings-Import fehlgeschlagen: Datei war leer oder ungültig.", true);
                return;
            }

            await SettingsService.SaveStaffelSettingsAsync(imported);
            _staffelSettings = await SettingsService.GetStaffelSettingsAsync();
            SetMaintenanceStatus("StaffelSettings wurden importiert.", false);
        }
        catch (Exception ex)
        {
            SetMaintenanceStatus($"StaffelSettings-Import fehlgeschlagen: {ex.Message}", true);
        }
    }

    private static async Task<T?> ReadJsonFromInputFileAsync<T>(InputFileChangeEventArgs args, long maxSize)
    {
        var file = args.File;
        if (file is null)
        {
            return default;
        }

        await using var stream = file.OpenReadStream(maxAllowedSize: maxSize);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, _jsonOptions);
    }

    private async Task ConfirmRestoreBackupZipAsync(InputFileChangeEventArgs args)
    {
        var confirmed = await JS.InvokeAsync<bool>("confirm", "Soll die ausgewählte Backup-Datei wirklich eingespielt werden?");
        if (!confirmed)
        {
            return;
        }

        await RestoreBackupZipAsync(args);
    }

    private async Task RestoreBackupZipAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file is null)
        {
            SetMaintenanceStatus("Keine Backup-Datei ausgewählt.", true);
            return;
        }

        try
        {
            await using var uploadStream = file.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
            using var memory = new MemoryStream();
            await uploadStream.CopyToAsync(memory);
            memory.Position = 0;

            using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);

            var importedCount = 0;

            var sessionEntry = FindZipEntry(archive, "SessionData.json");
            if (sessionEntry is not null)
            {
                var session = await ReadZipJsonAsync<SessionData>(sessionEntry);
                if (session is not null)
                {
                    await MasterDataService.SaveSessionDataAsync(session);
                    importedCount++;
                }
            }

            var appEntry = FindZipEntry(archive, "AppSettings.json");
            if (appEntry is not null)
            {
                var appSettings = await ReadZipJsonAsync<AppSettings>(appEntry);
                if (appSettings is not null)
                {
                    SettingsSecrets.KeepExistingSecrets(appSettings, await SettingsService.GetAppSettingsAsync());
                    await SettingsService.SaveAppSettingsAsync(appSettings);
                    importedCount++;
                }
            }

            var staffelEntry = FindZipEntry(archive, "StaffelSettings.json");
            if (staffelEntry is not null)
            {
                var staffelSettings = await ReadZipJsonAsync<StaffelSettings>(staffelEntry);
                if (staffelSettings is not null)
                {
                    await SettingsService.SaveStaffelSettingsAsync(staffelSettings);
                    importedCount++;
                }
            }

            var archiveEntry = FindZipEntry(archive, "einsatz_archiv.json") ?? FindZipEntry(archive, "archiv-export.json");
            if (archiveEntry is not null)
            {
                var archiveBytes = await ReadZipBytesAsync(archiveEntry);
                var importedArchiveCount = await ArchivService.ImportFromJsonAsync(archiveBytes);
                if (importedArchiveCount > 0)
                {
                    importedCount++;
                }
            }

            _staffelSettings = await SettingsService.GetStaffelSettingsAsync();
            _appSettings = await SettingsService.GetAppSettingsAsync();
            EnsureQuickNoteTemplatesInitialized(_appSettings);
            _quickNoteTemplatesText = string.Join(Environment.NewLine, _appSettings.QuickNoteTemplates);
            _quickNoteList = new List<string>(_appSettings.QuickNoteTemplates);

            if (importedCount == 0)
            {
                SetMaintenanceStatus("Keine passenden Einstellungsdateien in der ZIP gefunden.", true);
                return;
            }

            SetMaintenanceStatus($"Backup erfolgreich eingespielt ({importedCount} Datei(en)).", false);
        }
        catch (Exception ex)
        {
            SetMaintenanceStatus($"Backup-Wiederherstellung fehlgeschlagen: {ex.Message}", true);
        }
    }

    private static ZipArchiveEntry? FindZipEntry(ZipArchive archive, string fileName)
    {
        return archive.Entries.FirstOrDefault(entry =>
            string.Equals(Path.GetFileName(entry.FullName), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<T?> ReadZipJsonAsync<T>(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, _jsonOptions);
    }

    private static async Task<byte[]> ReadZipBytesAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private string GetDownloadUrl(string relativePath)
    {
        return new Uri(new Uri(Navigation.BaseUri), relativePath.TrimStart('/')).ToString();
    }

    [Inject] private LeitungAuthStore LeitungAuthStore { get; set; } = default!;
    [Inject] private VpnDeviceService VpnDevices { get; set; } = default!;
    [Inject] private VpnDeviceStore VpnDeviceStore { get; set; } = default!;

    private sealed record VpnDeviceRow(VpnDevice Device, DateTime? LastHandshakeUtc);

    private VpnServerInfo? _vpnServer;
    private List<VpnDeviceRow> _vpnDevices = [];
    private string _vpnEndpoint = string.Empty;
    private string _vpnName = string.Empty;
    private int _vpnValidityDays;
    private string _vpnPassword = string.Empty;
    private bool _vpnBusy;
    private string _vpnMessage = string.Empty;
    private bool _vpnError;
    private VpnDeviceCreated? _vpnCreated;
    private string _vpnQrDataUrl = string.Empty;
    private string _vpnConfigDataUrl = string.Empty;
    private string _vpnConfigFileName = string.Empty;

    private async Task LoadVpnAsync()
    {
        if (!VpnDevices.IsAvailable) return;
        try
        {
            _vpnServer = await VpnDevices.GetServerInfoAsync();
            _vpnEndpoint = VpnDeviceStore.Endpoint;
            if (string.IsNullOrWhiteSpace(_vpnEndpoint) && _vpnServer is not null)
            {
                // Vorschlag: oeffentlicher Name der Team-Ansicht + WireGuard-Port
                var host = new Uri(string.IsNullOrWhiteSpace(_appSettings.MobileBaseUrl) ? Navigation.BaseUri
                    : (_appSettings.MobileBaseUrl.Contains("://") ? _appSettings.MobileBaseUrl : "https://" + _appSettings.MobileBaseUrl)).Host;
                _vpnEndpoint = $"{host}:{_vpnServer.ListenPort}";
            }
            var status = (await VpnDevices.GetStatusAsync()).ToDictionary(s => s.PublicKey, s => s.LastHandshakeUtc);
            _vpnDevices = VpnDeviceStore.Devices
                .Select(d => new VpnDeviceRow(d, status.GetValueOrDefault(d.PublicKey)))
                .OrderBy(d => d.Device.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            SetVpnMessage($"VPN-Status nicht lesbar: {ex.Message}", true);
        }
        await InvokeAsync(StateHasChanged);
    }

    private void SaveVpnEndpoint()
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(_vpnEndpoint.Trim(), @"^[A-Za-z0-9.\-]+:\d{1,5}$"))
        {
            SetVpnMessage("Bitte als Name:Port angeben, z. B. dein-einsatz.mywire.org:51820.", true);
            return;
        }
        VpnDeviceStore.SetEndpoint(_vpnEndpoint);
        SetVpnMessage("Adresse gespeichert.", false);
    }

    private async Task CreateVpnDeviceAsync()
    {
        // Der QR-Code enthaelt einen privaten Schluessel – deshalb das Passwort trotz Anmeldung noch einmal
        if (!LeitungAuthStore.Verify(_vpnPassword))
        {
            SetVpnMessage("Das Passwort ist falsch.", true);
            return;
        }
        _vpnPassword = string.Empty;
        _vpnBusy = true;
        try
        {
            if (string.IsNullOrWhiteSpace(VpnDeviceStore.Endpoint)) SaveVpnEndpoint();
            var now = DateTime.UtcNow;
            DateTime? expires = _vpnValidityDays > 0 ? now.AddDays(_vpnValidityDays) : null;
            _vpnCreated = await VpnDevices.CreateAsync(_vpnName, expires, now);
            _vpnQrDataUrl = GenerateQrDataUrl(_vpnCreated.ClientConfig);
            _vpnConfigDataUrl = "data:text/plain;charset=utf-8," + Uri.EscapeDataString(_vpnCreated.ClientConfig);
            _vpnConfigFileName = "einsatz-" + new string(_vpnCreated.Device.Name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()) + ".conf";
            _vpnName = string.Empty;
            SetVpnMessage(string.Empty, false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            SetVpnMessage(ex.Message, true);
        }
        finally
        {
            _vpnBusy = false;
        }
    }

    private async Task CloseVpnCreated()
    {
        // Privaten Schluessel nicht laenger als noetig im Speicher halten
        _vpnCreated = null;
        _vpnQrDataUrl = _vpnConfigDataUrl = string.Empty;
        await LoadVpnAsync();
    }

    private async Task RemoveVpnDeviceAsync(VpnDevice device)
    {
        if (!await JS.InvokeAsync<bool>("confirm", $"„{device.Name}“ sperren? Das Gerät kommt danach nicht mehr ins VPN.")) return;
        try
        {
            await VpnDevices.RemoveAsync(device.PublicKey);
            SetVpnMessage($"„{device.Name}“ gesperrt.", false);
        }
        catch (InvalidOperationException ex)
        {
            SetVpnMessage(ex.Message, true);
        }
        await LoadVpnAsync();
    }

    private void SetVpnMessage(string message, bool isError)
    {
        _vpnMessage = message;
        _vpnError = isError;
    }

    private static string GenerateQrDataUrl(string content)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCoder.QRCodeGenerator.ECCLevel.M);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(6);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }

    private string _pwCurrent = string.Empty;
    private string _pwNew = string.Empty;
    private string _pwRepeat = string.Empty;
    private string _pwError = string.Empty;

    private Task ChangeLeitungPasswordAsync()
    {
        if (LeitungAuthStore.ChangePassword(_pwCurrent, _pwNew, _pwRepeat) is { } error)
        {
            _pwError = error;
            return Task.CompletedTask;
        }

        // Auch diese Anmeldung ist jetzt ungueltig – direkt mit dem neuen Passwort neu anmelden
        _pwCurrent = _pwNew = _pwRepeat = _pwError = string.Empty;
        Navigation.NavigateTo($"{LeitungAuth.LoginPath}?geaendert=true", forceLoad: true);
        return Task.CompletedTask;
    }

    // Die LiveTracking-App haengt /api/CollarWebhook/... selbst an – daher die Basisadresse ohne Schraegstrich am Ende
    private string LiveTrackingServerUrl => Navigation.BaseUri.TrimEnd('/');

    [Inject] private LiveTrackingAccess LiveTrackingAccess { get; set; } = default!;

    private string _liveTrackingCode = string.Empty;
    private bool _liveTrackingNoFingerprint;

    private async Task LoadLiveTrackingCodeAsync()
    {
        // Fingerabdruck des Zertifikats, das dieser Browser gerade sieht – genau das bekommt auch die App
        var baseUri = new Uri(Navigation.BaseUri);
        var fingerprint = await ServerCertificateProbe.GetSha256Async(baseUri);
        _liveTrackingNoFingerprint = fingerprint is null && baseUri.Scheme == Uri.UriSchemeHttps;
        _liveTrackingCode = LiveTrackingAccess.BuildConnectionCode(LiveTrackingServerUrl, fingerprint);
        await InvokeAsync(StateHasChanged);
    }

    private async Task RegenerateLiveTrackingKeyAsync()
    {
        var confirmed = await JS.InvokeAsync<bool>("confirm",
            "Neuen API-Schlüssel erzeugen? Die LiveTracking-App verbindet sich danach erst wieder mit dem neuen Verbindungscode.");
        if (!confirmed) return;

        LiveTrackingAccess.Regenerate();
        await LoadLiveTrackingCodeAsync();
        _copyFeedback = "Neuer Schlüssel erzeugt – Verbindungscode in der App neu eintragen.";
    }

    private string _copyFeedback = string.Empty;

    private async Task CopyToClipboardAsync(string text, string label)
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);
            _copyFeedback = $"{label} kopiert.";
        }
        catch (JSException)
        {
            // Zwischenablage nur ueber HTTPS verfuegbar – Feld ist markierbar
            _copyFeedback = "Kopieren nicht möglich – bitte das Feld anklicken und mit Strg+C kopieren.";
        }
    }

    private void SetMaintenanceStatus(string message, bool isError)
    {
        _maintenanceStatus = message;
        _maintenanceError = isError;
    }

    private void SetLogoStatus(string message, bool isError)
    {
        _logoStatus = message;
        _logoStatusIsError = isError;
    }

    private void SetPdfLogoStatus(string message, bool isError)
    {
        _pdfLogoStatus = message;
        _pdfLogoStatusIsError = isError;
    }

    private async Task UploadPdfLogoAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file is null)
        {
            SetPdfLogoStatus("Keine Datei ausgewählt.", true);
            return;
        }

        var extension = Path.GetExtension(file.Name)?.ToLowerInvariant() ?? string.Empty;
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp"
        };

        if (!allowed.Contains(extension))
        {
            SetPdfLogoStatus("Ungültiges Format. Erlaubt sind PNG, JPG, JPEG oder WEBP.", true);
            return;
        }

        try
        {
            var logoDirectory = Path.Combine(AppPathResolver.GetDataDirectory(), "logos");
            Directory.CreateDirectory(logoDirectory);

            var targetPath = Path.Combine(logoDirectory, $"pdf-logo{extension}");
            await using var source = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
            await using var target = File.Create(targetPath);
            await source.CopyToAsync(target);

            _staffelSettings.PdfLogoPfad = targetPath;
            await SettingsService.SaveStaffelSettingsAsync(_staffelSettings);

            SetPdfLogoStatus("PDF-Logo wurde hochgeladen und gespeichert.", false);
            _status = "Einstellungen gespeichert.";
        }
        catch (Exception ex)
        {
            SetPdfLogoStatus($"PDF-Logo-Upload fehlgeschlagen: {ex.Message}", true);
        }
    }

    private async Task ClearPdfLogoAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_staffelSettings.PdfLogoPfad) && File.Exists(_staffelSettings.PdfLogoPfad))
            {
                File.Delete(_staffelSettings.PdfLogoPfad);
            }

            _staffelSettings.PdfLogoPfad = string.Empty;
            await SettingsService.SaveStaffelSettingsAsync(_staffelSettings);
            SetPdfLogoStatus("PDF-Logo wurde entfernt.", false);
        }
        catch (Exception ex)
        {
            SetPdfLogoStatus($"PDF-Logo konnte nicht entfernt werden: {ex.Message}", true);
        }
    }

    // ── Traccar testen ────────────────────────────────────────────────
    [Inject] private TraccarTestService TraccarTest { get; set; } = default!;
    [Inject] private Microsoft.Extensions.Options.IOptionsMonitor<TeamMobileOptions> TeamMobileOptionen { get; set; } = default!;
    private string? _traccarTestKennung;
    private string _traccarTestEndpoint = string.Empty;
    private string _traccarTestLink = string.Empty;
    private string _traccarTestQr = string.Empty;
    private bool _traccarTestAbonniert;

    private void NeuerTraccarTest()
    {
        // Oeffentliche Adresse (wie im Team-QR-Code), sonst die Adresse dieser Seite
        var basis = !string.IsNullOrWhiteSpace(_appSettings.MobileBaseUrl) ? _appSettings.MobileBaseUrl
            : !string.IsNullOrWhiteSpace(TeamMobileOptionen.CurrentValue.PublicBaseUrl) ? TeamMobileOptionen.CurrentValue.PublicBaseUrl!
            : Navigation.BaseUri;
        _traccarTestEndpoint = basis.TrimEnd('/') + "/api/team-mobile/traccar";
        _traccarTestKennung = TraccarTest.NeueKennung(DateTime.UtcNow);
        _traccarTestLink = TraccarTestService.EinrichtungsLink(_traccarTestEndpoint, _traccarTestKennung, _appSettings);
        _traccarTestQr = GenerateQrDataUrl(_traccarTestLink);
        if (!_traccarTestAbonniert)
        {
            TraccarTest.Geaendert += OnTraccarTestEmpfang;
            _traccarTestAbonniert = true;
        }
    }

    private void OnTraccarTestEmpfang() => _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        if (_traccarTestAbonniert) TraccarTest.Geaendert -= OnTraccarTestEmpfang;
    }

    // ── Systemzustand (Zertifikat, Backup, Speicher, Divera, Uhr) ─────────
    [Inject] private SystemZustandService SystemZustand { get; set; } = default!;
    private bool _systemPruefBusy;

    private async Task PruefeSystemAsync()
    {
        _systemPruefBusy = true;
        try { await SystemZustand.PruefenAsync(); }
        finally { _systemPruefBusy = false; }
    }

    private async Task TestDiveraConnectionAsync()
    {
        _diveraTestBusy = true;
        _diveraTestStatus = string.Empty;
        _diveraTestError = false;

        try
        {
            // Zuerst aktuelle Settings speichern damit DiveraService den neuen Key hat
            await SettingsService.SaveAppSettingsAsync(_appSettings);
            await DiveraService.RefreshConfigurationAsync();

            if (!DiveraService.IsConfigured)
            {
                _diveraTestStatus = "Bitte API-Key eingeben und Integration aktivieren.";
                _diveraTestError = true;
                return;
            }

            var success = await DiveraService.TestConnectionAsync();
            if (success)
            {
                _diveraTestStatus = "Verbindung erfolgreich! Divera 24/7 ist erreichbar.";
                _diveraTestError = false;
            }
            else
            {
                _diveraTestStatus = "Verbindung fehlgeschlagen. Bitte API-Key prüfen.";
                _diveraTestError = true;
            }
        }
        catch (Exception ex)
        {
            _diveraTestStatus = $"Fehler: {ex.Message}";
            _diveraTestError = true;
        }
        finally
        {
            _diveraTestBusy = false;
        }
    }

    private static void EnsureQuickNoteTemplatesInitialized(AppSettings settings)
    {
        settings.QuickNoteTemplates ??= GetDefaultQuickNoteTemplates();

        if (settings.QuickNoteTemplates.Count == 0)
        {
            settings.QuickNoteTemplates = GetDefaultQuickNoteTemplates();
        }
    }

    private static List<string> ParseQuickNoteTemplates(string raw)
    {
        return raw
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetDefaultQuickNoteTemplates()
    {
        return new List<string>
        {
            "ELW Ankunft Einsatzstelle",
            "ELW verlässt Einsatzstelle",
            "Team vor Ort eingetroffen",
            "Lagemeldung an Leitstelle",
            "Suche gestartet",
            "Suche beendet"
        };
    }

    private async Task PlayFirstWarningTestAsync()
    {
        var p = BrowserPrefs.Preferences;
        await PlayWarningTestAsync(
            p.FirstWarningSound,
            p.FirstWarningFrequency,
            repeat: false,
            repeatSeconds: 0,
            "Test für erste Warnung abgespielt.");
    }

    private async Task PlayCriticalWarningTestAsync()
    {
        var p = BrowserPrefs.Preferences;
        await PlayWarningTestAsync(
            p.SecondWarningSound,
            p.SecondWarningFrequency,
            p.RepeatSecondWarning,
            Math.Max(1, p.RepeatWarningIntervalSeconds),
            "Test für kritische Warnung gestartet.");
    }

    private async Task StopWarningTestAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("layoutTools.stopWarningAlert");
            SetAudioTestStatus("Akustik-Test gestoppt.", false);
        }
        catch (Exception ex)
        {
            SetAudioTestStatus($"Test konnte nicht gestoppt werden: {ex.Message}", true);
        }
    }

    private async Task PlayWarningTestAsync(string soundType, int frequency, bool repeat, int repeatSeconds, string successMessage)
    {
        try
        {
            var started = await JS.InvokeAsync<bool>(
                "layoutTools.playWarningAlert",
                soundType,
                frequency,
                BrowserPrefs.Preferences.SoundVolume,
                repeat,
                repeatSeconds);

            if (!started)
            {
                SetAudioTestStatus("Audio ist im Browser blockiert. Bitte einmal auf die Seite klicken und erneut testen.", true);
                return;
            }

            SetAudioTestStatus(successMessage, false);
        }
        catch (Exception ex)
        {
            SetAudioTestStatus($"Akustik-Test fehlgeschlagen: {ex.Message}", true);
        }
    }

    private void SetAudioTestStatus(string message, bool isError)
    {
        _audioTestStatus = message;
        _audioTestError = isError;
    }

    // ── Tastenkürzel ─────────────────────────────────────────────────────

    private void StartRecording(string field) => _recordingField = field;
    private void StopRecording() => _recordingField = null;

    private async Task RecordKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e, string field)
    {
        if (e.Key is "Tab" or "Escape") { _recordingField = null; return; }
        var parts = new List<string>();
        if (e.CtrlKey)  parts.Add("ctrl");
        if (e.ShiftKey) parts.Add("shift");
        if (e.AltKey)   parts.Add("alt");
        parts.Add(e.Key.ToLower());
        SetShortcut(field, string.Join("+", parts));
        _recordingField = null;
        await SaveShortcutsAsync();
    }

    private void SetShortcut(string field, string value)
    {
        switch (field)
        {
            case "NavHome":     _shortcuts.NavHome     = value; break;
            case "NavKarte":    _shortcuts.NavKarte    = value; break;
            case "NavMonitor":  _shortcuts.NavMonitor  = value; break;
            case "NavStart":    _shortcuts.NavStart    = value; break;
            case "StepperUp":   _shortcuts.StepperUp   = value; break;
            case "StepperDown": _shortcuts.StepperDown = value; break;
        }
    }

    private void ResetShortcut(string field)
    {
        var defaults = new KeyboardShortcutPreferences();
        SetShortcut(field, field switch
        {
            "NavHome"     => defaults.NavHome,
            "NavKarte"    => defaults.NavKarte,
            "NavMonitor"  => defaults.NavMonitor,
            "NavStart"    => defaults.NavStart,
            "StepperUp"   => defaults.StepperUp,
            "StepperDown" => defaults.StepperDown,
            _ => string.Empty
        });
    }

    private void ResetAllShortcuts()
    {
        _shortcuts = new KeyboardShortcutPreferences();
        _ = SaveShortcutsAsync();
    }

    private async Task SaveShortcutsAsync()
    {
        BrowserPrefs.Update(p => p.Shortcuts = _shortcuts);
        await BrowserPrefs.SaveAsync();
        await JS.InvokeVoidAsync("keyboardShortcuts.configure", new
        {
            navHome    = _shortcuts.NavHome,
            navKarte   = _shortcuts.NavKarte,
            navMonitor = _shortcuts.NavMonitor,
            navStart   = _shortcuts.NavStart
        });
        await InvokeAsync(StateHasChanged);
    }

    private static string FormatShortcut(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return "–";
        return string.Join("+", shortcut.Split('+').Select(p => p switch
        {
            "ctrl"       => "Ctrl",
            "shift"      => "Shift",
            "alt"        => "Alt",
            "arrowup"    => "↑",
            "arrowdown"  => "↓",
            "arrowleft"  => "←",
            "arrowright" => "→",
            "escape"     => "Esc",
            "enter"      => "↵",
            "space"      => "Leertaste",
            "backspace"  => "⌫",
            "delete"     => "Entf",
            "tab"        => "Tab",
            var k        => k.Length == 1 ? k.ToUpper() : k
        }));
    }
}

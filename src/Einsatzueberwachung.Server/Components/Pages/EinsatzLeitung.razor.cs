using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Components;
using Einsatzueberwachung.Server.Services.Radio;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzLeitung : IDisposable
{
    [Inject] private IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IWeatherService Weather { get; set; } = default!;
    [Inject] private IRadioService Radio { get; set; } = default!;
    [Inject] private IMasterDataService MasterDataService { get; set; } = default!;

    private string _activeTab = "uebersicht";
    private string _noteFilter = "alle";
    private string? _busyTeamId;

    private EinsatzData _e => EinsatzService.CurrentEinsatz;
    private int _teamsRunning => EinsatzService.Teams.Count(t => t.IsRunning);
    private int _teamsWarning => EinsatzService.Teams.Count(t => t.IsFirstWarning || t.IsSecondWarning);
    private int _teamsReady => EinsatzService.Teams.Count(t => !t.IsRunning && !t.IsPausing);
    private int _teamsCritical => EinsatzService.Teams.Count(t => t.IsSecondWarning);

    private bool HasActiveEinsatz =>
        !string.IsNullOrWhiteSpace(_e.Einsatzort)
        || _e.AlarmierungsZeit.HasValue
        || EinsatzService.Teams.Count > 0;

    private bool _logoVisible = true;
    private void OnLogoError() => _logoVisible = false;

    // Dashboard-Listen, sortiert nach Eskalation/Belastung
    private IReadOnlyList<Team> RunningTeams => EinsatzService.Teams
        .Where(t => t.IsRunning)
        .OrderByDescending(t => t.IsSecondWarning)
        .ThenByDescending(t => t.IsFirstWarning)
        .ThenByDescending(t => t.ElapsedTime)
        .ThenBy(t => t.TeamName)
        .ToList();

    private IReadOnlyList<Team> ReadyTeams => EinsatzService.Teams
        .Where(t => !t.IsRunning && !t.IsPausing && !t.IsManuallyPaused)
        .OrderBy(t => t.TeamName)
        .ToList();

    private IReadOnlyList<Team> PauseTeams => EinsatzService.Teams
        .Where(t => t.IsPausing || t.IsManuallyPaused)
        .OrderBy(t => t.TeamName)
        .ToList();

    // Kontaktbeamter (Polizei) aus VermisstenInfo
    private bool HasKontaktbeamter
    {
        get
        {
            var v = EinsatzService.CurrentEinsatz.VermisstenInfo;
            return v is not null && !string.IsNullOrWhiteSpace(v.PolizeiKontaktName);
        }
    }

    private VermisstenInfo _vi = new();
    private bool MehrereVermisstErlaubt =>
        EinsatzService.CurrentEinsatz.Szenario.AllowsMultipleVermisste();

    private async Task SelectVermisstenAsync(Guid id)
    {
        if (_vi.Id == id) return;
        await FlushPendingAutoSaveAsync();
        var entry = EinsatzService.CurrentEinsatz.Vermisste?.FirstOrDefault(v => v.Id == id);
        if (entry is not null)
        {
            CloneViFrom(entry);
            await EnsureChecklistAsync();
        }
    }

    private async Task AddVermisstenAsync()
    {
        await FlushPendingAutoSaveAsync();
        _vi = new VermisstenInfo { Id = Guid.NewGuid() };
        await EinsatzService.UpsertVermisstenAsync(_vi);
        _lastAutoSavedAt = DateTime.Now;
    }

    private async Task RemoveCurrentVermisstenAsync()
    {
        var idToRemove = _vi.Id;
        _autoSaveCts?.Cancel();
        _autoSavePending = false;
        await EinsatzService.RemoveVermisstenAsync(idToRemove);
        var remaining = EinsatzService.CurrentEinsatz.Vermisste;
        if (remaining is { Count: > 0 })
            CloneViFrom(remaining[0]);
        else
            _vi = new VermisstenInfo { Id = Guid.NewGuid() };
    }

    private async Task FlushPendingAutoSaveAsync()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;
        if (!_autoSavePending) return;
        _autoSavePending = false;
        try
        {
            await EinsatzService.UpsertVermisstenAsync(_vi);
            _lastAutoSavedAt = DateTime.Now;
        }
        catch { /* swallow */ }
    }
    private string _saveMessage = string.Empty;
    private bool _saveIsError;
    private DateTime? _lastAutoSavedAt;
    private bool _autoSavePending;
    private CancellationTokenSource? _autoSaveCts;

    private string _elNotizText = string.Empty;
    private string _elNotizPrefix = string.Empty;
    private IReadOnlyList<MentionSuggestion> _mentionSuggestions = Array.Empty<MentionSuggestion>();

    // Akustik
    private bool _soundEnabled;
    private int _previousCriticalCount;
    private int _previousWarningCount;

    // Wetter
    private WeatherData? _weather;
    private DateTime? _weatherLoadedAt;
    private System.Threading.Timer? _weatherTimer;

    // Funk-FAB
    private bool _funkOpen;
    private string _funkText = string.Empty;
    private string? _funkRecipientTeamName;

    // Push-Toast
    private readonly List<PushToastItem> _pushToasts = new();
    private DateTime _pageOpenedAt = DateTime.Now;

    protected override void OnInitialized()
    {
        var list = EinsatzService.CurrentEinsatz.Vermisste;
        if (list is { Count: > 0 })
            CloneViFrom(list[0]);
        else
            _vi.Id = Guid.NewGuid();

        _ = EnsureChecklistAsync();

        RebuildMentionSuggestions();

        EinsatzService.EinsatzChanged += OnStateChanged;
        EinsatzService.TeamAdded += OnTeamChanged;
        EinsatzService.TeamUpdated += OnTeamChanged;
        EinsatzService.TeamRemoved += OnTeamChanged;
        EinsatzService.NoteAdded += OnNoteChanged;
        EinsatzService.VermisstenInfoChanged += OnVermisstenChanged;
        EinsatzService.SzenarioChanged += OnStateChangedDirect;
        EinsatzService.ElNotizAdded += OnStateChangedDirect;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            await JS.InvokeVoidAsync("elDashboard.startClock", "el-live-clock");
            _soundEnabled = await JS.InvokeAsync<bool>("elDashboard.getPref", "sound", false);
            _previousCriticalCount = _teamsCritical;
            _previousWarningCount = _teamsWarning;
            _ = LoadWeatherAsync();
            _weatherTimer = new System.Threading.Timer(
                _ => _ = LoadWeatherAsync(),
                null,
                TimeSpan.FromMinutes(15),
                TimeSpan.FromMinutes(15));
            await InvokeAsync(StateHasChanged);
        }
        catch { /* JS-Fehler dürfen die Seite nicht kippen */ }
    }

    // ── Vermisst: manueller + Auto-Save ──────────────────────────
    private async Task SaveVermisstenAsync()
    {
        await EinsatzService.UpsertVermisstenAsync(_vi);
        _saveMessage = "Gespeichert.";
        _saveIsError = false;
        _lastAutoSavedAt = DateTime.Now;
        await InvokeAsync(StateHasChanged);
        await Task.Delay(2000);
        _saveMessage = string.Empty;
        await InvokeAsync(StateHasChanged);
    }

    private void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        _autoSavePending = true;
        var snapshotId = _vi.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500, token);
                if (token.IsCancellationRequested) return;
                if (_vi.Id != snapshotId) return; // anderer Vermisster aktiv
                await EinsatzService.UpsertVermisstenAsync(_vi);
                _lastAutoSavedAt = DateTime.Now;
                _autoSavePending = false;
                _saveIsError = false;
                await InvokeAsync(StateHasChanged);
            }
            catch (TaskCanceledException) { /* überschrieben */ }
            catch (Exception)
            {
                _autoSavePending = false;
                _saveIsError = true;
                _saveMessage = "Fehler beim Speichern.";
                await InvokeAsync(StateHasChanged);
            }
        });
    }

    private string AutoSaveStatusText
    {
        get
        {
            if (_autoSavePending) return "Speichere…";
            if (_lastAutoSavedAt is null) return string.Empty;
            var diff = DateTime.Now - _lastAutoSavedAt.Value;
            if (diff.TotalSeconds < 5) return "Gespeichert";
            if (diff.TotalMinutes < 1) return $"Gespeichert vor {(int)diff.TotalSeconds}s";
            return $"Gespeichert {_lastAutoSavedAt.Value:HH:mm}";
        }
    }

    // ── EL-Notizen ───────────────────────────────────────────────
    private async Task AddElNotizAsync()
    {
        if (string.IsNullOrWhiteSpace(_elNotizText)) return;
        await EinsatzService.AddElNotizAsync(_elNotizText, _elNotizPrefix);
        _elNotizText = string.Empty;
    }

    private async Task DeleteElNotizAsync(string id)
    {
        await EinsatzService.DeleteElNotizAsync(id);
    }

    private void RebuildMentionSuggestions()
    {
        _mentionSuggestions = EinsatzService.Teams
            .Select(t => new MentionSuggestion(
                t.TeamId,
                t.TeamName,
                MentionType.Team,
                string.IsNullOrWhiteSpace(t.SearchAreaName) ? t.HundefuehrerName : t.SearchAreaName))
            .ToList();
    }

    // ── Quick-Actions ────────────────────────────────────────────
    private async Task StartTeamAsync(string teamId)
    {
        if (_busyTeamId is not null) return;
        _busyTeamId = teamId;
        try { await EinsatzService.StartTeamTimerAsync(teamId); }
        finally { _busyTeamId = null; }
    }

    private async Task PauseTeamAsync(string teamId)
    {
        if (_busyTeamId is not null) return;
        _busyTeamId = teamId;
        try { await EinsatzService.PauseTeamTimerAsync(teamId); }
        finally { _busyTeamId = null; }
    }

    private async Task StopTeamAsync(string teamId)
    {
        if (_busyTeamId is not null) return;
        _busyTeamId = teamId;
        try { await EinsatzService.StopTeamTimerAsync(teamId); }
        finally { _busyTeamId = null; }
    }

    private async Task ResetTeamAsync(string teamId)
    {
        if (_busyTeamId is not null) return;
        _busyTeamId = teamId;
        try { await EinsatzService.ResetTeamTimerAsync(teamId); }
        finally { _busyTeamId = null; }
    }

    // ── Belastungs-Vorwarnung ────────────────────────────────────
    private (int? minutes, bool alreadyInWarning) MinutesUntilThreshold(Team t)
    {
        if (!t.IsRunning) return (null, false);
        if (t.IsSecondWarning) return (null, true);
        var elapsed = (int)Math.Floor(t.ElapsedTime.TotalMinutes);
        var threshold = t.IsFirstWarning ? t.SecondWarningMinutes : t.FirstWarningMinutes;
        var diff = threshold - elapsed;
        return (Math.Max(diff, 0), t.IsFirstWarning);
    }

    private string? PreWarningText(Team t)
    {
        var (mins, inWarning) = MinutesUntilThreshold(t);
        if (mins is null) return null;
        if (mins > 5) return null;
        var label = inWarning ? "Kritisch in" : "Warnung in";
        return mins == 0 ? $"{label} <1 min" : $"{label} {mins} min";
    }

    // ── Lagestreifen ─────────────────────────────────────────────
    private string StatusbarClass
    {
        get
        {
            if (_teamsCritical > 0) return "el-statusbar-critical";
            if (_teamsWarning > 0) return "el-statusbar-warn";
            return "el-statusbar-idle";
        }
    }

    private string StatusbarIcon
    {
        get
        {
            if (_teamsCritical > 0) return "bi-exclamation-octagon-fill";
            if (_teamsWarning > 0) return "bi-exclamation-triangle-fill";
            if (_teamsRunning > 0) return "bi-broadcast";
            return "bi-circle";
        }
    }

    private string StatusbarText
    {
        get
        {
            if (_teamsCritical > 0)
                return _teamsCritical == 1 ? "1 Team kritisch" : $"{_teamsCritical} Teams kritisch";
            if (_teamsWarning > 0)
                return _teamsWarning == 1 ? "1 Team in Warnung" : $"{_teamsWarning} Teams in Warnung";
            if (_teamsRunning > 0)
                return _teamsRunning == 1 ? "1 Team aktiv" : $"{_teamsRunning} Teams aktiv";
            return "Keine Teams im Einsatz";
        }
    }

    private string StatusbarDetail
    {
        get
        {
            var team = LongestEscalatedTeam();
            if (team is not null) return team.TeamName;

            var preWarn = EinsatzService.Teams
                .Where(t => t.IsRunning && !t.IsFirstWarning && !t.IsSecondWarning)
                .Select(t => new { Team = t, Mins = MinutesUntilThreshold(t).minutes })
                .Where(x => x.Mins is not null && x.Mins <= 5)
                .OrderBy(x => x.Mins)
                .FirstOrDefault();
            if (preWarn is not null && preWarn.Mins is not null)
                return $"{preWarn.Team.TeamName} → Schwelle in {preWarn.Mins} min";
            return string.Empty;
        }
    }

    private TimeSpan? LongestActiveTimer
    {
        get
        {
            var team = LongestEscalatedTeam();
            return team?.ElapsedTime;
        }
    }

    private Team? LongestEscalatedTeam()
    {
        var pool = EinsatzService.Teams.Where(t => t.IsSecondWarning).ToList();
        if (pool.Count == 0) pool = EinsatzService.Teams.Where(t => t.IsFirstWarning).ToList();
        if (pool.Count == 0) return null;
        return pool.OrderByDescending(t => t.ElapsedTime).FirstOrDefault();
    }

    // ── Akustik-Toggle ────────────────────────────────────────────
    private async Task ToggleSoundAsync()
    {
        _soundEnabled = !_soundEnabled;
        try
        {
            await JS.InvokeVoidAsync("elDashboard.setPref", "sound", _soundEnabled);
            if (_soundEnabled)
                await JS.InvokeVoidAsync("elDashboard.beep", 100, 1320, 0.2);
        }
        catch { /* swallow */ }
    }

    private async Task PlayAlertIfEscalatedAsync()
    {
        if (!_soundEnabled) return;
        bool escalated =
            _teamsCritical > _previousCriticalCount
            || (_teamsWarning > _previousWarningCount && _teamsCritical == _previousCriticalCount);
        _previousCriticalCount = _teamsCritical;
        _previousWarningCount = _teamsWarning;
        if (!escalated) return;
        try { await JS.InvokeVoidAsync("elDashboard.alertSound"); }
        catch { /* swallow */ }
    }

    // ── Wetter ───────────────────────────────────────────────────
    private async Task LoadWeatherAsync()
    {
        try
        {
            WeatherData? data = null;
            var pos = _e.ElwPosition;
            if (pos.HasValue)
            {
                data = await Weather.GetCurrentWeatherAsync(pos.Value.Latitude, pos.Value.Longitude);
            }
            else if (!string.IsNullOrWhiteSpace(_e.Einsatzort))
            {
                data = await Weather.GetCurrentWeatherByAddressAsync(_e.Einsatzort);
            }
            _weather = data;
            _weatherLoadedAt = DateTime.Now;
            await InvokeAsync(StateHasChanged);
        }
        catch { /* schluck */ }
    }

    // ── Funk-FAB ─────────────────────────────────────────────────
    private void OpenFunk(string? recipientTeamName = null)
    {
        _funkRecipientTeamName = recipientTeamName;
        _funkText = string.Empty;
        _funkOpen = true;
    }

    private void CloseFunk()
    {
        _funkOpen = false;
        _funkText = string.Empty;
        _funkRecipientTeamName = null;
    }

    private async Task SendFunkAsync()
    {
        if (string.IsNullOrWhiteSpace(_funkText)) return;
        var text = _funkText.Trim();
        if (!string.IsNullOrWhiteSpace(_funkRecipientTeamName))
        {
            text = $"@{_funkRecipientTeamName} {text}";
        }
        var req = new CreateRadioMessageRequest(
            text,
            "einsatzleitung",
            "Einsatzleitung",
            "Einsatzleitung");
        try
        {
            await Radio.AddMessageAsync(req);
            _funkOpen = false;
            _funkText = string.Empty;
            _funkRecipientTeamName = null;
        }
        catch
        {
            _saveMessage = "Funk konnte nicht gesendet werden.";
            _saveIsError = true;
        }
    }

    // ── Push-Toast ───────────────────────────────────────────────
    private void DismissToast(string id) => _pushToasts.RemoveAll(t => t.Id == id);

    private async Task AutoDismissToastAsync(string id)
    {
        await Task.Delay(6000);
        _pushToasts.RemoveAll(t => t.Id == id);
        await InvokeAsync(StateHasChanged);
    }

    private sealed class PushToastItem
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string Title { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string Icon { get; init; } = "bi-broadcast";
        public DateTime CreatedAt { get; init; } = DateTime.Now;
    }

    // ── Belastungs-Tachometer ────────────────────────────────────
    public sealed record BahnData(
        double ElapsedPct,
        double Warn1Pct,
        string Status,
        string StatusLabel);

    private BahnData BelastungsBahn(Team t)
    {
        var thr1 = Math.Max(t.FirstWarningMinutes, 1);
        var thr2 = Math.Max(t.SecondWarningMinutes, thr1 + 1);
        var elapsedMin = t.ElapsedTime.TotalMinutes;
        var elapsedPct = Math.Clamp(elapsedMin / thr2 * 100.0, 0, 100);
        var warn1Pct = Math.Clamp((double)thr1 / thr2 * 100.0, 0, 100);

        string status, label;
        if (t.IsSecondWarning)      { status = "kritisch"; label = "Kritisch"; }
        else if (t.IsFirstWarning)  { status = "warnung";  label = "Warnung"; }
        else if (t.IsRunning)       { status = "aktiv";    label = "Aktiv"; }
        else if (t.IsPausing)       { status = "pause";    label = "Pause"; }
        else                        { status = "bereit";   label = "Bereit"; elapsedPct = 0; }

        return new BahnData(elapsedPct, warn1Pct, status, label);
    }

    private static string Pct(double v)
        => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string Frac(double v)
        => (v / 100.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

    // ── Funkstammbuch-Tape (letzte Funkmeldungen) ────────────────
    private List<GlobalNotesEntry> FunktapeEntries(int max = 6)
        => EinsatzService.GlobalNotes
            .Where(n => string.Equals(n.SourceType, "Funk", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.Timestamp)
            .Take(max)
            .ToList();

    // ── Sektions-Fortschritt im Vermisst-Tab ─────────────────────
    private (int filled, int total) PersonProgress => CountFilled(
        _vi.Vorname, _vi.Nachname, _vi.Geburtsdatum, _vi.Alter, _vi.Kleidung, _vi.Besonderheiten);

    private (int filled, int total) OrtProgress => CountFilled(
        _vi.ZuletztGesehenOrt, _vi.ZuletztGesehenZeit, _vi.ZuletztGesehenVon);

    private (int filled, int total) MedizinProgress
    {
        get
        {
            var f = 0;
            var total = 6;
            if (_vi.Orientierung != OrientierungsStatus.Unbekannt) f++;
            if (_vi.Mobilitaet != MobilitaetsStatus.Unbekannt) f++;
            if (_vi.Suizidrisiko != RisikoStatus.Unbekannt) f++;
            if (_vi.Bewaffnet != RisikoStatus.Unbekannt) f++;
            if (!string.IsNullOrWhiteSpace(_vi.Vorerkrankungen)) f++;
            if (!string.IsNullOrWhiteSpace(_vi.Medikamente)) f++;
            return (f, total);
        }
    }

    private (int filled, int total) PolizeiProgress
    {
        get
        {
            var f = 0;
            if (!string.IsNullOrWhiteSpace(_vi.PolizeiKontaktName)) f++;
            if (!string.IsNullOrWhiteSpace(_vi.PolizeiDienstnummer)) f++;
            if (!string.IsNullOrWhiteSpace(_vi.PolizeiTelefon)) f++;
            if (_vi.PolizeiVermisstenmeldungAufgenommen) f++;
            if (_vi.PolizeiKoordinationBesprochen) f++;
            if (_vi.PolizeiSuchabschnittAbgestimmt) f++;
            if (_vi.PolizeiRueckmeldepflichtVereinbart) f++;
            if (_vi.PolizeiDatenschutzGeklaert) f++;
            return (f, 8);
        }
    }

    private (int filled, int total) BosProgress
    {
        get
        {
            var f = 0;
            if (!string.IsNullOrWhiteSpace(_vi.BosEinheit)) f++;
            if (!string.IsNullOrWhiteSpace(_vi.BosZugfuehrer)) f++;
            if (!string.IsNullOrWhiteSpace(_vi.BosFunkrufname)) f++;
            if (!string.IsNullOrWhiteSpace(_vi.BosAufgabenteilung)) f++;
            if (_vi.BosAbschnittAbgestimmt) f++;
            if (_vi.BosRessourcenBesprochen) f++;
            return (f, 6);
        }
    }

    private static (int filled, int total) CountFilled(params string?[] values)
    {
        var filled = values.Count(v => !string.IsNullOrWhiteSpace(v));
        return (filled, values.Length);
    }

    private static string SectionProgressClass((int filled, int total) p)
    {
        if (p.filled == 0) return string.Empty;
        if (p.filled >= p.total) return "el-section-progress-good";
        return string.Empty;
    }

    // ── Notiz-Stream (Funkstammbuch + EL-Notizen) ─────────────────
    private List<NoteStreamItem> BuildNoteStream()
    {
        var items = new List<NoteStreamItem>();

        var elNotizen = EinsatzService.CurrentEinsatz.ElNotizen ?? new();
        var globalNotes = EinsatzService.GlobalNotes;

        if (_noteFilter == "alle" || _noteFilter == "el" || _noteFilter == "wichtig")
        {
            foreach (var n in elNotizen)
            {
                if (_noteFilter == "wichtig" && !string.Equals(n.Prefix, "Wichtig", StringComparison.OrdinalIgnoreCase))
                    continue;
                items.Add(new NoteStreamItem
                {
                    Id = n.Id,
                    Text = n.Text,
                    TimeFormatted = n.FormattedDateTime,
                    Timestamp = n.Timestamp,
                    Source = "EL",
                    Prefix = n.Prefix,
                    PrefixIcon = string.IsNullOrWhiteSpace(n.Prefix) ? null : n.PrefixIcon,
                    IsShared = false,
                    IsDeletable = true
                });
            }
        }

        if (_noteFilter == "alle" || _noteFilter == "funk" || _noteFilter == "wichtig")
        {
            foreach (var n in globalNotes)
            {
                var isFunk = n.Type == GlobalNotesEntryType.Manual
                             && string.Equals(n.SourceType, "Funk", StringComparison.OrdinalIgnoreCase);
                if (_noteFilter == "funk" && !isFunk) continue;
                if (_noteFilter == "wichtig") continue;

                items.Add(new NoteStreamItem
                {
                    Id = n.Id,
                    Text = n.Text,
                    TimeFormatted = n.FormattedTimestamp,
                    Timestamp = n.Timestamp,
                    Source = string.IsNullOrWhiteSpace(n.SourceTeamName) ? "System" : n.SourceTeamName,
                    Prefix = isFunk ? "Funk" : string.Empty,
                    PrefixIcon = isFunk ? "bi-broadcast" : null,
                    IsShared = true,
                    IsDeletable = false
                });
            }
        }

        return items.OrderByDescending(i => i.Timestamp).ToList();
    }

    private sealed class NoteStreamItem
    {
        public string Id { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public string TimeFormatted { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public string Source { get; init; } = string.Empty;
        public string Prefix { get; init; } = string.Empty;
        public string? PrefixIcon { get; init; }
        public bool IsShared { get; init; }
        public bool IsDeletable { get; init; }
    }

    // ── Helpers ──────────────────────────────────────────────────
    private void CloneViFrom(VermisstenInfo src)
    {
        _vi = new VermisstenInfo
        {
            Id = src.Id == Guid.Empty ? Guid.NewGuid() : src.Id,
            Vorname = src.Vorname,
            Nachname = src.Nachname,
            Alter = src.Alter,
            Geburtsdatum = src.Geburtsdatum,
            Kleidung = src.Kleidung,
            Besonderheiten = src.Besonderheiten,
            ZuletztGesehenOrt = src.ZuletztGesehenOrt,
            ZuletztGesehenZeit = src.ZuletztGesehenZeit,
            ZuletztGesehenVon = src.ZuletztGesehenVon,
            Vorerkrankungen = src.Vorerkrankungen,
            Medikamente = src.Medikamente,
            Orientierung = src.Orientierung,
            Mobilitaet = src.Mobilitaet,
            Suizidrisiko = src.Suizidrisiko,
            Bewaffnet = src.Bewaffnet,
            PolizeiKontaktName = src.PolizeiKontaktName,
            PolizeiDienstnummer = src.PolizeiDienstnummer,
            PolizeiTelefon = src.PolizeiTelefon,
            PolizeiVermisstenmeldungAufgenommen = src.PolizeiVermisstenmeldungAufgenommen,
            PolizeiKoordinationBesprochen = src.PolizeiKoordinationBesprochen,
            PolizeiSuchabschnittAbgestimmt = src.PolizeiSuchabschnittAbgestimmt,
            PolizeiRueckmeldepflichtVereinbart = src.PolizeiRueckmeldepflichtVereinbart,
            PolizeiDatenschutzGeklaert = src.PolizeiDatenschutzGeklaert,
            BosEinheit = src.BosEinheit,
            BosZugfuehrer = src.BosZugfuehrer,
            BosFunkrufname = src.BosFunkrufname,
            BosAufgabenteilung = src.BosAufgabenteilung,
            BosAbschnittAbgestimmt = src.BosAbschnittAbgestimmt,
            BosRessourcenBesprochen = src.BosRessourcenBesprochen,
            Checkliste = CloneChecklist(src.Checkliste)
        };
    }

    private static ChecklistInstance? CloneChecklist(ChecklistInstance? src)
    {
        if (src is null) return null;
        return new ChecklistInstance
        {
            TemplateId = src.TemplateId,
            TemplateName = src.TemplateName,
            Szenario = src.Szenario,
            Items = src.Items.Select(it => new ChecklistItemDefinition
            {
                Id = it.Id,
                Label = it.Label,
                Type = it.Type,
                Choices = new List<string>(it.Choices),
                Required = it.Required
            }).ToList(),
            Values = new Dictionary<string, string?>(src.Values)
        };
    }

    // Checklist-Item Getter/Setter (Values im Dict sind Strings)
    private bool ChecklistGetBool(Guid itemId)
    {
        if (_vi.Checkliste is null) return false;
        return _vi.Checkliste.Values.TryGetValue(itemId.ToString(), out var v)
               && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    private void ChecklistSetBool(Guid itemId, bool value)
    {
        if (_vi.Checkliste is null) return;
        _vi.Checkliste.Values[itemId.ToString()] = value ? "true" : "false";
        ScheduleAutoSave();
    }

    private string ChecklistGetText(Guid itemId)
    {
        if (_vi.Checkliste is null) return string.Empty;
        return _vi.Checkliste.Values.TryGetValue(itemId.ToString(), out var v) ? v ?? string.Empty : string.Empty;
    }

    private void ChecklistSetText(Guid itemId, string? value)
    {
        if (_vi.Checkliste is null) return;
        _vi.Checkliste.Values[itemId.ToString()] = value ?? string.Empty;
        ScheduleAutoSave();
    }

    private (int filled, int total) ChecklistProgress
    {
        get
        {
            if (_vi.Checkliste is null) return (0, 0);
            var total = _vi.Checkliste.Items.Count;
            var filled = _vi.Checkliste.Items.Count(it =>
            {
                var raw = _vi.Checkliste.Values.TryGetValue(it.Id.ToString(), out var v) ? v : null;
                if (string.IsNullOrWhiteSpace(raw)) return false;
                if (it.Type == ChecklistItemType.Bool) return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
                return true;
            });
            return (filled, total);
        }
    }

    private async Task EnsureChecklistAsync()
    {
        if (_vi.Checkliste is not null) return;
        var szenario = EinsatzService.CurrentEinsatz.Szenario;
        if (szenario == EinsatzSzenarioType.Unbestimmt) return;
        var template = await MasterDataService.GetDefaultChecklistTemplateAsync(szenario);
        if (template is null) return;
        _vi.Checkliste = ChecklistInstance.FromTemplate(template);
    }

    private void OnGeburtsdatumChanged(ChangeEventArgs e)
    {
        var value = e.Value?.ToString() ?? string.Empty;
        _vi.Geburtsdatum = value;
        if (DateTime.TryParseExact(value, "dd.MM.yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var birth))
        {
            var today = DateTime.Today;
            var age = today.Year - birth.Year;
            if (birth.Date > today.AddYears(-age)) age--;
            _vi.Alter = age.ToString();
        }
        ScheduleAutoSave();
    }

    private static string GetTimerClass(Team t) => t switch
    {
        _ when t.IsSecondWarning => "el-timer-critical",
        _ when t.IsFirstWarning  => "el-timer-warning",
        _ when t.IsRunning       => "el-timer-running",
        _                        => "el-timer-default"
    };

    private static string GetTeamStatusColor(Team t) => t switch
    {
        _ when t.IsSecondWarning => "var(--belastung-2)",
        _ when t.IsFirstWarning  => "var(--belastung-1)",
        _ when t.IsRunning       => "var(--einsatz-aktiv)",
        _ when t.IsPausing       => "var(--pause-grau)",
        _                        => "var(--bereit)"
    };

    private static string OrientierungLabel(OrientierungsStatus s) => s switch
    {
        OrientierungsStatus.Gut => "Gut",
        OrientierungsStatus.Eingeschraenkt => "Eingeschränkt",
        _ => "Unbekannt"
    };

    private static string MobilitaetLabel(MobilitaetsStatus s) => s switch
    {
        MobilitaetsStatus.ZuFuss => "Zu Fuß",
        MobilitaetsStatus.Rollator => "Rollator",
        MobilitaetsStatus.Rollstuhl => "Rollstuhl",
        MobilitaetsStatus.Fahrzeug => "Fahrzeug",
        _ => "Unbekannt"
    };

    private void OnStateChanged()
    {
        _ = PlayAlertIfEscalatedAsync();
        InvokeAsync(StateHasChanged);
    }

    private void OnTeamChanged(Team team)
    {
        RebuildMentionSuggestions();
        _ = PlayAlertIfEscalatedAsync();
        InvokeAsync(StateHasChanged);
    }

    private void OnNoteChanged(GlobalNotesEntry note)
    {
        if (note.Timestamp >= _pageOpenedAt
            && string.Equals(note.SourceType, "Funk", StringComparison.OrdinalIgnoreCase))
        {
            var item = new PushToastItem
            {
                Title = string.IsNullOrWhiteSpace(note.SourceTeamName) ? "Funk" : $"Funk · {note.SourceTeamName}",
                Body = note.Text.Length > 140 ? note.Text[..137] + "…" : note.Text,
                Icon = "bi-broadcast"
            };
            _pushToasts.Add(item);
            _ = AutoDismissToastAsync(item.Id);

            if (_soundEnabled)
            {
                _ = JS.InvokeVoidAsync("elDashboard.beep", 90, 660, 0.2).AsTask();
            }
        }
        InvokeAsync(StateHasChanged);
    }

    private void OnStateChangedDirect()
        => InvokeAsync(StateHasChanged);

    private bool _szenarioMenuOpen;

    private async Task SetSzenarioAsync(EinsatzSzenarioType szenario)
    {
        _szenarioMenuOpen = false;

        if (szenario == EinsatzSzenarioType.Mantrailer)
        {
            var current = EinsatzService.CurrentEinsatz.Vermisste?.Count ?? 0;
            if (current > 1)
            {
                _saveMessage = $"Hinweis: Mantrailer-Szenario gewählt — es sind {current} Vermisste eingetragen. " +
                               "Auf der Mantrailer-Seite wird nur die erste Person bearbeitbar angezeigt; die übrigen bleiben in den Daten erhalten.";
                _saveIsError = false;
            }
        }

        await EinsatzService.UpdateSzenarioAsync(szenario);
    }

    private void OnVermisstenChanged()
    {
        if (_activeTab != "vermisst")
        {
            var latest = EinsatzService.CurrentEinsatz.VermisstenInfo;
            if (latest is not null)
                CloneViFrom(latest);
        }
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        EinsatzService.EinsatzChanged -= OnStateChanged;
        EinsatzService.TeamAdded -= OnTeamChanged;
        EinsatzService.TeamUpdated -= OnTeamChanged;
        EinsatzService.TeamRemoved -= OnTeamChanged;
        EinsatzService.NoteAdded -= OnNoteChanged;
        EinsatzService.VermisstenInfoChanged -= OnVermisstenChanged;
        EinsatzService.SzenarioChanged -= OnStateChangedDirect;
        EinsatzService.ElNotizAdded -= OnStateChangedDirect;
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _weatherTimer?.Dispose();
    }
}

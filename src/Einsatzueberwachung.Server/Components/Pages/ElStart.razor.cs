using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Divera;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

/// <summary>
/// Einsatz starten auf Tablet/Handy (EL-Ansicht, 1.11): nur das Noetigste gross und mit dem Daumen bedienbar –
/// Divera-Alarm, Szenario, Einsatz/Uebung, Ort (auch per GPS), Stichwort, Einsatzleiter. Rest eingeklappt.
/// </summary>
public partial class ElStart : IDisposable
{
    private const string LastEinsatzleiterKey = "einsatz.lastEinsatzleiter";

    [Inject] private IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] private IMasterDataService MasterDataService { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private IDiveraService DiveraService { get; set; } = default!;
    [Inject] private ITimeService TimeService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private static readonly EinsatzSzenarioType[] Szenarien =
        [EinsatzSzenarioType.Mantrailer, EinsatzSzenarioType.Flaeche, EinsatzSzenarioType.Truemmer, EinsatzSzenarioType.Sonstige];

    private readonly EinsatzData _model = new() { AnzahlTeams = 1, IstEinsatz = true };
    private List<PersonalEntry> _personal = [];
    private List<DiveraAlarm> _diveraAlarms = [];
    private int? _selectedDiveraId;
    private bool _einsatzBereitsAktiv;
    private DateTime _now;
    private TimeOnly? _alarmTime;
    private Timer? _clockTimer;

    private bool _szenarioMissing;
    private bool _einsatzortMissing;
    private bool _locating;
    private string _locationInfo = string.Empty;
    private bool _locationError;
    private string _status = string.Empty;
    private bool _error;
    private bool _busy;

    private IEnumerable<PersonalEntry> ActivePersonnel => _personal
        .Where(p => p.IsActive)
        .OrderBy(p => p.Nachname)
        .ThenBy(p => p.Vorname);

    private IEnumerable<PersonalEntry> EinsatzleiterOptions => ActivePersonnel.Where(p => p.Skills.HasFlag(PersonalSkills.Einsatzleiter));

    private IEnumerable<PersonalEntry> FuehrungsassistentOptions => ActivePersonnel.Where(p => p.Skills.HasFlag(PersonalSkills.Fuehrungsassistent));

    protected override async Task OnInitializedAsync()
    {
        _now = TimeService.Now;
        var einsatz = EinsatzService.CurrentEinsatz;
        _einsatzBereitsAktiv = !string.IsNullOrWhiteSpace(einsatz.Einsatzort) && einsatz.EinsatzEnde is null;
        if (_einsatzBereitsAktiv) return;

        _alarmTime = TimeOnly.FromDateTime(_now);
        _personal = await MasterDataService.GetPersonalListAsync();
        EinsatzStartHelper.ApplyStaffel(_model, await SettingsService.GetStaffelSettingsAsync());

        try
        {
            _diveraAlarms = DiveraService.IsConfigured ? await DiveraService.GetActiveAlarmsAsync() : [];
        }
        catch
        {
            // Divera nicht erreichbar: Einsatz laesst sich trotzdem von Hand starten
            _diveraAlarms = [];
        }

        _clockTimer = new Timer(_ => InvokeAsync(() => { _now = TimeService.Now; StateHasChanged(); }), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _einsatzBereitsAktiv) return;
        try
        {
            // Zuletzt verwendeter Einsatzleiter auf diesem Geraet
            var last = await JS.InvokeAsync<string?>("localStorage.getItem", LastEinsatzleiterKey);
            if (!string.IsNullOrWhiteSpace(last) && string.IsNullOrWhiteSpace(_model.Einsatzleiter))
            {
                _model.Einsatzleiter = last;
                StateHasChanged();
            }
        }
        catch (JSException)
        {
            // ohne localStorage (privater Modus) einfach leer lassen
        }
    }

    private void SelectSzenario(EinsatzSzenarioType szenario)
    {
        _model.Szenario = szenario;
        _szenarioMissing = false;
    }

    private void SetEinsatzleiter(string? id)
    {
        var person = _personal.FirstOrDefault(p => p.Id == id);
        if (person is not null) _model.Einsatzleiter = person.FullName;
    }

    private void ImportFromDivera(DiveraAlarm alarm)
    {
        EinsatzStartHelper.ApplyDiveraAlarm(_model, alarm);
        _selectedDiveraId = alarm.Id;
        if (alarm.Date != default) _alarmTime = TimeOnly.FromDateTime(alarm.Date);
        _einsatzortMissing = false;
        _locationInfo = alarm.Lat.HasValue ? "Ort und Koordinate aus dem Divera-Alarm übernommen." : "Ort aus dem Divera-Alarm übernommen.";
        _locationError = false;
    }

    private sealed record PositionResult(bool Ok, double Lat, double Lng, int Accuracy, string? Error);

    private async Task UseMyLocationAsync()
    {
        _locating = true;
        _locationInfo = string.Empty;
        try
        {
            var pos = await JS.InvokeAsync<PositionResult>("layoutTools.getCurrentPosition");
            if (!pos.Ok)
            {
                _locationInfo = pos.Error ?? "Standort nicht verfügbar.";
                _locationError = true;
                return;
            }

            EinsatzStartHelper.ApplyCoordinates(_model, pos.Lat, pos.Lng);
            if (string.IsNullOrWhiteSpace(_model.Einsatzort))
            {
                _model.Einsatzort = string.Format(System.Globalization.CultureInfo.InvariantCulture, "Standort {0:F5}, {1:F5}", pos.Lat, pos.Lng);
            }
            _einsatzortMissing = false;
            _locationInfo = $"Standort übernommen (Genauigkeit ± {pos.Accuracy} m) – die Karte zentriert sich darauf.";
            _locationError = false;
        }
        catch (JSException ex)
        {
            _locationInfo = $"Standort nicht verfügbar: {ex.Message}";
            _locationError = true;
        }
        finally
        {
            _locating = false;
        }
    }

    private async Task StartAsync()
    {
        _einsatzortMissing = string.IsNullOrWhiteSpace(_model.Einsatzort);
        _szenarioMissing = _model.Szenario == EinsatzSzenarioType.Unbestimmt;
        if (EinsatzStartHelper.Validate(_model) is { } error)
        {
            _status = error;
            _error = true;
            return;
        }

        _busy = true;
        _status = string.Empty;
        try
        {
            var now = TimeService.Now;
            _model.EinsatzDatum = now;
            if (_alarmTime is { } t)
            {
                var alarm = now.Date + t.ToTimeSpan();
                // Alarmzeit „23:50“ kurz nach Mitternacht gehoert zum Vortag
                _model.AlarmierungsZeit = alarm > now.AddMinutes(1) ? alarm.AddDays(-1) : alarm;
            }
            if (string.IsNullOrWhiteSpace(_model.MapAddress)) _model.MapAddress = _model.Einsatzort;

            await EinsatzService.StartEinsatzAsync(_model);

            if (!string.IsNullOrWhiteSpace(_model.Einsatzleiter))
            {
                try { await JS.InvokeVoidAsync("localStorage.setItem", LastEinsatzleiterKey, _model.Einsatzleiter); }
                catch (JSException) { /* nur Komfort */ }
            }

            Navigation.NavigateTo("/el");
        }
        catch (Exception ex) when (ex is not NavigationException)
        {
            _status = $"Fehler: {ex.Message}";
            _error = true;
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose() => _clockTimer?.Dispose();
}

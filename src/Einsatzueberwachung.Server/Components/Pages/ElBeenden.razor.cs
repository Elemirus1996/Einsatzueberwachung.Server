using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Components.Monitor;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;

namespace Einsatzueberwachung.Server.Components.Pages;

/// <summary>
/// Einsatz beenden auf Tablet/Handy (EL-Ansicht, 1.11): Ergebnis, Ende, Bemerkungen, Angaben fuer den Bericht
/// (wie im Monitor, APP_API EL Abschnitt 10), Vor-Ort-Liste, Bestaetigung.
/// Archivieren und PDF uebernimmt der gemeinsame <see cref="EinsatzAbschlussService"/> – wie im Monitor.
/// </summary>
public partial class ElBeenden : IDisposable
{
    [Inject] private IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] private IMasterDataService MasterDataService { get; set; } = default!;
    [Inject] private ITimeService TimeService { get; set; } = default!;
    [Inject] private EinsatzAbschlussService EinsatzAbschluss { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;

    private List<PersonalEntry> _personal = [];
    private List<DogEntry> _hunde = [];
    private HashSet<string> _personalIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _hundeIds = new(StringComparer.OrdinalIgnoreCase);

    private bool _einsatzAktiv;
    private DateTime _einsatzBeginn;
    private int _laufendeTeams;
    private int _trackCount;
    private DateTime _now;
    private Timer? _clockTimer;

    private string _ergebnisWahl = string.Empty;
    private TimeOnly? _endzeit;
    private string _bemerkungen = string.Empty;
    private bool _tracksImPdf;
    private int _punktCount;
    private bool _punkteImPdf;
    private Einsatzueberwachung.Domain.Models.Enums.MapTileType _kartenart = Einsatzueberwachung.Domain.Models.Enums.MapTileType.Streets;
    private bool _ressourcenGeprueft;
    private readonly MonitorAbschlussModel _abschluss = new();

    private bool _ergebnisFehlt;
    private bool _pruefungFehlt;
    private string _status = string.Empty;
    private bool _busy;
    private EinsatzAbschlussErgebnis? _ergebnis;

    private IEnumerable<PersonalEntry> ActivePersonnel =>
        SelectionListOrdering.OrderPersonnelByFirstName(_personal.Where(p => p.IsActive));

    private IEnumerable<DogEntry> ActiveDogs => _hunde
        .Where(d => d.IsActive)
        .OrderBy(d => d.Name);

    protected override async Task OnInitializedAsync()
    {
        _now = TimeService.Now;
        var einsatz = EinsatzService.CurrentEinsatz;
        _einsatzAktiv = !string.IsNullOrWhiteSpace(einsatz.Einsatzort) && einsatz.EinsatzEnde is null;
        if (!_einsatzAktiv) return;

        _einsatzBeginn = einsatz.EinsatzDatum;
        _laufendeTeams = EinsatzService.Teams.Count(t => t.IsRunning);
        _trackCount = einsatz.TrackSnapshots?.Count ?? 0;
        _punktCount = einsatz.MapMarkers?.Count ?? 0;
        _endzeit = TimeOnly.FromDateTime(_now);

        _personal = await MasterDataService.GetPersonalListAsync();
        _hunde = await MasterDataService.GetDogListAsync();
        (_personalIds, _hundeIds) = EinsatzAbschlussService.PreselectVorOrt(EinsatzService.Teams, ActivePersonnel, ActiveDogs);
        var gruende = AbschlussAngaben.Abbruchgruende(await SettingsService.GetAppSettingsAsync());
        _abschluss.Laden(einsatz, EinsatzService.Teams, _hunde, gruende);

        _clockTimer = new Timer(_ => InvokeAsync(() => { _now = TimeService.Now; StateHasChanged(); }), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    // Gespeichert wird der Text wie im Monitor; angezeigt mit Umlauten
    private static string ErgebnisText(string option) => Einsatzueberwachung.Domain.Services.ErgebnisText.Anzeige(option);

    private void SelectErgebnis(string option)
    {
        _ergebnisWahl = option;
        _ergebnisFehlt = false;
    }

    private static void Toggle(HashSet<string> ids, string id, ChangeEventArgs e)
    {
        if (e.Value is true) ids.Add(id);
        else ids.Remove(id);
    }

    private List<string> SelectedNames<T>(IEnumerable<T> entries, HashSet<string> ids, Func<T, string> id, Func<T, string> name) =>
        entries.Where(entry => ids.Contains(id(entry)))
            .Select(name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

    private async Task BeendenAsync()
    {
        _status = EinsatzAbschlussService.Validate(_ergebnisWahl, _ressourcenGeprueft) ?? string.Empty;
        _ergebnisFehlt = string.IsNullOrWhiteSpace(_ergebnisWahl);
        _pruefungFehlt = !_ergebnisFehlt && !_ressourcenGeprueft;
        if (_status.Length > 0) return;

        var fehlend = _abschluss.Fehlend(_ergebnisWahl);
        if (fehlend.Count > 0 && !_abschluss.UnvollstaendigBestaetigt)
        {
            _status = $"Es fehlen noch: {string.Join(", ", fehlend)}. Bitte eintragen oder „Trotzdem ohne diese Angaben beenden“ ankreuzen.";
            return;
        }

        _busy = true;
        try
        {
            _ergebnis = await EinsatzAbschluss.AbschliessenAsync(new EinsatzAbschlussAnfrage(
                _einsatzBeginn,
                _ergebnisWahl,
                _bemerkungen,
                _endzeit,
                _tracksImPdf,
                SelectedNames(ActivePersonnel, _personalIds, p => p.Id, p => p.FullName),
                SelectedNames(ActiveDogs, _hundeIds, d => d.Id, d => d.Name),
                _kartenart,
                PunkteImBericht: _punkteImPdf,
                Angaben: _abschluss.ZuEingabe(TimeService.Now, _ergebnisWahl),
                UnvollstaendigBestaetigt: true,
                Abschnitte: _abschluss.GewaehlteAbschnitte(_tracksImPdf)));
        }
        catch (Exception ex)
        {
            _status = $"Abschluss fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose() => _clockTimer?.Dispose();
}

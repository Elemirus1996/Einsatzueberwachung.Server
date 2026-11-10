using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Components;
using Einsatzueberwachung.Server.Components.Monitor;
using Einsatzueberwachung.Server.Components.Shared;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{    private void UpdateMissionTopbar()
    {
        if (HasActiveEinsatz)
        {
            MissionTopbar.SetContent(this, builder =>
            {
                builder.OpenComponent<MonitorPanelsMenu>(0);
                builder.AddAttribute(1, nameof(MonitorPanelsMenu.Layout), _currentLayout);
                builder.AddAttribute(2, nameof(MonitorPanelsMenu.OnTogglePanel),
                    EventCallback.Factory.Create<string>(this, TogglePanelVisibleAsync));
                builder.CloseComponent();
            });
        }
        else
        {
            MissionTopbar.ClearContent(this);
        }
    }

    private async Task TogglePanelVisibleAsync(string panelId)
    {
        var panel = _currentLayout.FirstOrDefault(p => p.PanelId == panelId);
        if (panel is null) return;
        panel.IsVisible = !panel.IsVisible;
        await SaveLayoutAsync();
    }

    private async Task SaveLayoutAsync()
    {
        await DashboardLayoutService.SaveLayoutAsync(
            EinsatzService.CurrentEinsatz.Fuehrungsassistent, _currentLayout);
    }

    private static string PanelLabel(string panelId) =>
        KnownPanels.Labels.TryGetValue(panelId, out var label) ? label : panelId;

    private static int TabletPanelOrder(string panelId) => panelId switch
    {
        KnownPanels.Teams => 0,
        KnownPanels.Notizen => 1,
        KnownPanels.Suchgebiete => 2,
        _ => 3
    };

    private void SelectTabletPanel(string panelId)
    {
        if (_currentLayout.Any(panel =>
                panel.IsVisible && !IsSummaryPanel(panel.PanelId) && panel.PanelId == panelId))
        {
            _tabletActivePanelId = panelId;
        }
    }

    private void ToggleSummaryGrid() => _summaryGridCollapsed = !_summaryGridCollapsed;

    private static bool IsSummaryPanel(string panelId) =>
        panelId is KnownPanels.EinsatzInfo or KnownPanels.Vermissten or KnownPanels.Wetter;

    private bool CanExpandSummaryDetails(string panelId) => panelId switch
    {
        KnownPanels.EinsatzInfo => true,
        KnownPanels.Vermissten => (EinsatzService.CurrentEinsatz.Vermisste?.Count ?? 0) > 0,
        KnownPanels.Wetter => _monitorWeather is not null,
        _ => false
    };

    private IReadOnlyList<MonitorCoordinationItem> GetCoordinationItems()
    {
        var einsatz = EinsatzService.CurrentEinsatz;
        var legacyContacts = einsatz.EinsatzkontakteMigriert
            ? new List<VermisstenInfo>()
            : einsatz.Vermisste ?? new();
        var items = new List<MonitorCoordinationItem>();

        bool IsConfirmed(bool currentValue, Func<VermisstenInfo, bool> legacyValue) =>
            currentValue || legacyContacts.Any(legacyValue);

        void Add(MonitorCoordinationItemId id, string label, bool isBos, bool isConfirmed) =>
            items.Add(new MonitorCoordinationItem(id, label, isBos, isConfirmed));

        Add(
            MonitorCoordinationItemId.PoliceVermisstenmeldungAufgenommen,
            "Vermisstenmeldung aufgenommen",
            false,
            IsConfirmed(einsatz.PolizeiVermisstenmeldungAufgenommen, person => person.PolizeiVermisstenmeldungAufgenommen));
        Add(
            MonitorCoordinationItemId.PoliceKoordinationBesprochen,
            "Einsatzkoordination besprochen",
            false,
            IsConfirmed(einsatz.PolizeiKoordinationBesprochen, person => person.PolizeiKoordinationBesprochen));
        Add(
            MonitorCoordinationItemId.PoliceSuchabschnittAbgestimmt,
            "Suchabschnitte abgestimmt",
            false,
            IsConfirmed(einsatz.PolizeiSuchabschnittAbgestimmt, person => person.PolizeiSuchabschnittAbgestimmt));
        Add(
            MonitorCoordinationItemId.PoliceRueckmeldepflichtVereinbart,
            "Rückmeldepflicht vereinbart",
            false,
            IsConfirmed(einsatz.PolizeiRueckmeldepflichtVereinbart, person => person.PolizeiRueckmeldepflichtVereinbart));
        Add(
            MonitorCoordinationItemId.PoliceDatenschutzGeklaert,
            "Datenschutz (Personendaten) geklärt",
            false,
            IsConfirmed(einsatz.PolizeiDatenschutzGeklaert, person => person.PolizeiDatenschutzGeklaert));

        if (!string.IsNullOrWhiteSpace(GetBosContactDetails()?.Einheit))
        {
            Add(
                MonitorCoordinationItemId.BosAbschnittAbgestimmt,
                "Einsatzabschnitt abgestimmt",
                true,
                IsConfirmed(einsatz.BosAbschnittAbgestimmt, person => person.BosAbschnittAbgestimmt));
            Add(
                MonitorCoordinationItemId.BosRessourcenBesprochen,
                "Ressourcen besprochen (Wärmebild etc.)",
                true,
                IsConfirmed(einsatz.BosRessourcenBesprochen, person => person.BosRessourcenBesprochen));
        }

        return items;
    }

    private void OpenCoordinationModal()
    {
        _editEinsatzMessage = string.Empty;
        _editEinsatzIsError = false;
        _showCoordinationModal = true;
    }

    private void CloseCoordinationModal() => _showCoordinationModal = false;

    private Task CompleteCoordinationFromModalAsync(MonitorCoordinationToggle toggle) =>
        SetCoordinationItemAsync(toggle.Id, toggle.IsConfirmed, delayNextSummaryItem: false);

    private Task CompleteCoordinationFromSummaryAsync(MonitorCoordinationToggle toggle) =>
        SetCoordinationItemAsync(toggle.Id, toggle.IsConfirmed, delayNextSummaryItem: true);

    private async Task SetCoordinationItemAsync(
        MonitorCoordinationItemId item,
        bool isConfirmed,
        bool delayNextSummaryItem)
    {
        if (_coordinationUpdateInProgress)
            return;

        _coordinationUpdateInProgress = true;
        try
        {
            PopulateEditEinsatzForm();
            switch (item)
            {
                case MonitorCoordinationItemId.PoliceVermisstenmeldungAufgenommen:
                    _editEinsatzForm.PolizeiVermisstenmeldungAufgenommen = isConfirmed;
                    break;
                case MonitorCoordinationItemId.PoliceKoordinationBesprochen:
                    _editEinsatzForm.PolizeiKoordinationBesprochen = isConfirmed;
                    break;
                case MonitorCoordinationItemId.PoliceSuchabschnittAbgestimmt:
                    _editEinsatzForm.PolizeiSuchabschnittAbgestimmt = isConfirmed;
                    break;
                case MonitorCoordinationItemId.PoliceRueckmeldepflichtVereinbart:
                    _editEinsatzForm.PolizeiRueckmeldepflichtVereinbart = isConfirmed;
                    break;
                case MonitorCoordinationItemId.PoliceDatenschutzGeklaert:
                    _editEinsatzForm.PolizeiDatenschutzGeklaert = isConfirmed;
                    break;
                case MonitorCoordinationItemId.BosAbschnittAbgestimmt:
                    _editEinsatzForm.BosAbschnittAbgestimmt = isConfirmed;
                    break;
                case MonitorCoordinationItemId.BosRessourcenBesprochen:
                    _editEinsatzForm.BosRessourcenBesprochen = isConfirmed;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(item), item, "Unbekannter Abstimmungspunkt.");
            }

            await SaveEditEinsatzAsync();
            if (_editEinsatzIsError)
            {
                if (!_showCoordinationModal)
                    _showEditEinsatzModal = true;
                return;
            }

            if (delayNextSummaryItem)
                await Task.Delay(TimeSpan.FromMilliseconds(650));
        }
        finally
        {
            _coordinationUpdateInProgress = false;
        }
    }

    private IReadOnlyList<MonitorPoliceContactSummary> GetPoliceContactSummaries()
    {
        var einsatz = EinsatzService.CurrentEinsatz;
        if (HasPoliceContact(einsatz.PolizeiKontaktName, einsatz.PolizeiDienstnummer, einsatz.PolizeiTelefon))
        {
            return
            [
                new MonitorPoliceContactSummary(
                    "Polizei-Kontakt",
                    einsatz.PolizeiKontaktName,
                    einsatz.PolizeiDienstnummer,
                    einsatz.PolizeiTelefon)
            ];
        }

        if (einsatz.EinsatzkontakteMigriert)
        {
            return [];
        }

        var vermisste = einsatz.Vermisste;
        if (vermisste is null)
        {
            return [];
        }
        return vermisste
            .Where(person => HasPoliceContact(person.PolizeiKontaktName, person.PolizeiDienstnummer, person.PolizeiTelefon))
            .Select(person => new MonitorPoliceContactSummary(
                string.IsNullOrWhiteSpace(person.VollerName) ? "Vermisste Person" : person.VollerName,
                person.PolizeiKontaktName,
                person.PolizeiDienstnummer,
                person.PolizeiTelefon))
            .ToArray();
    }

    private MonitorBosContactSummary? GetBosContactDetails()
    {
        var einsatz = EinsatzService.CurrentEinsatz;
        var globalContact = new MonitorBosContactSummary(
            einsatz.BosEinheit,
            einsatz.BosZugfuehrer,
            einsatz.BosFunkrufname,
            einsatz.BosAufgabenteilung);
        if (globalContact.HasData || einsatz.EinsatzkontakteMigriert)
        {
            return globalContact.HasData ? globalContact : null;
        }

        return einsatz.Vermisste?
            .Select(person => new MonitorBosContactSummary(
                person.BosEinheit,
                person.BosZugfuehrer,
                person.BosFunkrufname,
                person.BosAufgabenteilung))
            .FirstOrDefault(contact => contact.HasData);
    }

    private static string FormatBosContactSummary(MonitorBosContactSummary? contact) =>
        contact is null
            ? string.Empty
            : FormatContact(
                contact.Einheit ?? string.Empty,
                contact.Zugfuehrer ?? string.Empty,
                contact.Funkrufname ?? string.Empty,
                contact.Aufgabenteilung ?? string.Empty);

    private static string FormatContact(params string[] values) =>
        string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static bool HasPoliceContact(string? contactName, string? serviceNumber, string? phone) =>
        !string.IsNullOrWhiteSpace(contactName)
        || !string.IsNullOrWhiteSpace(serviceNumber)
        || !string.IsNullOrWhiteSpace(phone);

    private static string FirstNonEmpty(string currentValue, IEnumerable<string> legacyValues) =>
        !string.IsNullOrWhiteSpace(currentValue)
            ? currentValue
            : legacyValues.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private bool IsSummaryDetailsExpanded(string panelId) => panelId switch
    {
        KnownPanels.EinsatzInfo => _einsatzInfoDetailsExpanded,
        KnownPanels.Vermissten => _vermisstenDetailsExpanded,
        KnownPanels.Wetter => _wetterDetailsExpanded,
        _ => false
    };

    private void ToggleSummaryDetails(string panelId)
    {
        switch (panelId)
        {
            case KnownPanels.EinsatzInfo:
                _einsatzInfoDetailsExpanded = !_einsatzInfoDetailsExpanded;
                break;
            case KnownPanels.Vermissten:
                _vermisstenDetailsExpanded = !_vermisstenDetailsExpanded;
                break;
            case KnownPanels.Wetter:
                _wetterDetailsExpanded = !_wetterDetailsExpanded;
                break;
        }
    }

    // ===== Bestehende Methoden (unverändert) =====

    private IEnumerable<PersonalEntry> ActivePersonnel =>
        SelectionListOrdering.OrderPersonnelByFirstName(_personalList.Where(person => person.IsActive));

    private IEnumerable<DogEntry> ActiveDogs => _dogList
        .Where(dog => dog.IsActive)
        .OrderBy(dog => dog.Name);

    private List<string> SelectedPersonnelNames => ActivePersonnel
        .Where(person => _selectedPersonnelIds.Contains(person.Id))
        .Select(person => person.FullName)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name)
        .ToList();

    private List<string> SelectedDogNames => ActiveDogs
        .Where(dog => _selectedDogIds.Contains(dog.Id))
        .Select(dog => dog.Name)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name)
        .ToList();

    private IEnumerable<PersonalEntry> HundefuehrerOptions
    {
        get
        {
            var allHandlers = ActivePersonnel
                .Where(person => person.Skills.HasFlag(PersonalSkills.Hundefuehrer));

            if (!string.IsNullOrWhiteSpace(_teamForm.DogId))
            {
                var selectedDog = _dogList.FirstOrDefault(d => d.Id == _teamForm.DogId);
                if (selectedDog != null && selectedDog.HundefuehrerIds.Count > 0)
                {
                    return allHandlers.Where(p => selectedDog.HundefuehrerIds.Contains(p.Id));
                }
            }

            return allHandlers;
        }
    }

    private IEnumerable<PersonalEntry> HelferOptions => ActivePersonnel
        .Where(person => person.Skills.HasFlag(PersonalSkills.Helfer) || person.Skills.HasFlag(PersonalSkills.Hundefuehrer));

    private IEnumerable<PersonalEntry> DrohnenpilotOptions => ActivePersonnel
        .Where(person => person.Skills.HasFlag(PersonalSkills.Drohnenpilot));

    private IEnumerable<DogEntry> AvailableDogs => _dogList
        .Where(dog => dog.IsActive && (string.IsNullOrWhiteSpace(_teamForm.HundefuehrerId) || dog.HundefuehrerIds.Contains(_teamForm.HundefuehrerId)))
        .OrderBy(dog => dog.Name);

    private IEnumerable<DroneEntry> AvailableDrones => _droneList
        .Where(drone => drone.IsActive)
        .OrderBy(drone => drone.Name)
        .ThenBy(drone => drone.Modell);


}

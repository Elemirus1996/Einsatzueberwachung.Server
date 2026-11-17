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
        var items = new List<MonitorCoordinationItem>();

        void Add(MonitorCoordinationItemId id, string label, bool isBos, bool isConfirmed) =>
            items.Add(new MonitorCoordinationItem(id, label, isBos, isConfirmed));

        Add(
            MonitorCoordinationItemId.PoliceVermisstenmeldungAufgenommen,
            "Vermisstenmeldung aufgenommen",
            false,
            einsatz.PolizeiVermisstenmeldungAufgenommen);
        Add(
            MonitorCoordinationItemId.PoliceKoordinationBesprochen,
            "Einsatzkoordination besprochen",
            false,
            einsatz.PolizeiKoordinationBesprochen);
        Add(
            MonitorCoordinationItemId.PoliceSuchabschnittAbgestimmt,
            "Suchabschnitte abgestimmt",
            false,
            einsatz.PolizeiSuchabschnittAbgestimmt);
        Add(
            MonitorCoordinationItemId.PoliceRueckmeldepflichtVereinbart,
            "Rückmeldepflicht vereinbart",
            false,
            einsatz.PolizeiRueckmeldepflichtVereinbart);
        Add(
            MonitorCoordinationItemId.PoliceDatenschutzGeklaert,
            "Datenschutz (Personendaten) geklärt",
            false,
            einsatz.PolizeiDatenschutzGeklaert);

        if (!string.IsNullOrWhiteSpace(GetBosContactDetails()?.Einheit))
        {
            Add(
                MonitorCoordinationItemId.BosAbschnittAbgestimmt,
                "Einsatzabschnitt abgestimmt",
                true,
                einsatz.BosAbschnittAbgestimmt);
            Add(
                MonitorCoordinationItemId.BosRessourcenBesprochen,
                "Ressourcen besprochen (Wärmebild etc.)",
                true,
                einsatz.BosRessourcenBesprochen);
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

        return [];
    }

    private MonitorBosContactSummary? GetBosContactDetails()
    {
        var einsatz = EinsatzService.CurrentEinsatz;
        var globalContact = new MonitorBosContactSummary(
            einsatz.BosEinheit,
            einsatz.BosZugfuehrer,
            einsatz.BosFunkrufname,
            einsatz.BosAufgabenteilung);
        return globalContact.HasData ? globalContact : null;
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

    private IEnumerable<PersonalEntry> TeamPersonnelOptions
    {
        get
        {
            var resources = EinsatzService.GetEinsatzResources();
            return SelectionListOrdering.OrderPersonnelByFirstName(_personalList.Where(person =>
                person.IsActive
                && (IsPersonSelectedForCurrentTeam(person.Id)
                    || IsInTeamPlanningRoster(resources, ResourceEntityType.Person, person.Id))));
        }
    }

    private IEnumerable<PersonalEntry> HundefuehrerOptions
    {
        get
        {
            var allHandlers = TeamPersonnelOptions
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

    private IEnumerable<PersonalEntry> HelferOptions => TeamPersonnelOptions
        .Where(person => person.Skills.HasFlag(PersonalSkills.Helfer) || person.Skills.HasFlag(PersonalSkills.Hundefuehrer));

    private IEnumerable<PersonalEntry> DrohnenpilotOptions => TeamPersonnelOptions
        .Where(person => person.Skills.HasFlag(PersonalSkills.Drohnenpilot));

    private IEnumerable<DogEntry> AvailableDogs
    {
        get
        {
            var resources = EinsatzService.GetEinsatzResources();
            return _dogList
                .Where(dog => dog.IsActive
                    && (_teamForm.DogId == dog.Id
                        || IsInTeamPlanningRoster(resources, ResourceEntityType.Dog, dog.Id))
                    && (string.IsNullOrWhiteSpace(_teamForm.HundefuehrerId)
                        || dog.HundefuehrerIds.Contains(_teamForm.HundefuehrerId)))
                .OrderBy(dog => dog.Name);
        }
    }

    private static bool IsInTeamPlanningRoster(
        IReadOnlyList<EinsatzResource> resources,
        ResourceEntityType entityType,
        string entityId) =>
        resources.Count == 0
        || resources.Any(resource =>
            resource.EntityType == entityType
            && string.Equals(resource.EntityId, entityId, StringComparison.Ordinal));

    private bool IsPersonSelectedForCurrentTeam(string personId) =>
        _teamForm.HundefuehrerId == personId
        || _teamForm.HelferId == personId
        || _teamForm.HelferId2 == personId
        || _teamForm.HelferId3 == personId;

    private IEnumerable<DroneEntry> AvailableDrones => _droneList
        .Where(drone => drone.IsActive)
        .OrderBy(drone => drone.Name)
        .ThenBy(drone => drone.Modell);


}

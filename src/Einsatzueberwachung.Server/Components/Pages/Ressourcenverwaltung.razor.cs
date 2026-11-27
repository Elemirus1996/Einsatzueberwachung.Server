using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Components.Shared;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Components;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class Ressourcenverwaltung
{
    [Inject] private IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] private IMasterDataService MasterDataService { get; set; } = default!;
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private DiveraResourceSyncService DiveraResourceSyncService { get; set; } = default!;

    private AppSettings _appSettings = new();
    private List<PersonalEntry> _personnel = new();
    private List<DogEntry> _dogs = new();
    private Dictionary<string, ResourceStatus> _pendingStatuses = new(StringComparer.Ordinal);
    private Dictionary<string, string> _pendingNotes = new(StringComparer.Ordinal);
    private ResourceStatus _initialStatus = ResourceStatus.AufAnfahrt;
    private string _searchText = string.Empty;
    private string _newDiveraUserId = string.Empty;
    private string _message = string.Empty;
    private bool _messageIsError;
    private bool _showQuickCreate;
    private bool _showQuickCreateDog;
    private bool _checkInExpanded;
    private bool _rosterFiltersExpanded;
    private bool _hideRosterDogs;
    private bool _isSaving;
    private string _rosterStateFilter = ResourceRosterFilter.AllResourceStates;
    private string _rosterHandlerAssignmentFilter = ResourceRosterFilter.AllHandlerAssignments;
    private ResourceRosterItem? _resourceRemovalCandidate;
    private string _resourceRemovalError = string.Empty;
    private PersonalEntry _newPerson = new();
    private DogEntry _newDog = new();

    private bool HasActiveEinsatz =>
        !string.IsNullOrWhiteSpace(EinsatzService.CurrentEinsatz.Einsatzort)
        && EinsatzService.CurrentEinsatz.EinsatzEnde is null;

    private IReadOnlyList<EinsatzResource> CheckedInResources => EinsatzService.GetEinsatzResources();

    private IReadOnlyList<ResourceRosterGroup> RosterGroups =>
        ResourceRosterBuilder.Build(CheckedInResources, _personnel, _dogs);

    private IReadOnlyList<FilteredResourceGroup> FilteredRosterGroups =>
        ResourceRosterFilter.Apply(
            RosterGroups,
            _dogs,
            _rosterStateFilter,
            _rosterHandlerAssignmentFilter,
            _hideRosterDogs);

    private int ActiveRosterFilterCount =>
        ResourceRosterFilter.CountActiveFilters(
            _rosterStateFilter,
            _rosterHandlerAssignmentFilter,
            _hideRosterDogs);

    private int TotalResourceCount => CheckedInResources.Count;

    private int ActivePersonnelCount => _personnel.Count(person => person.IsActive);
    private bool IsQuickCreateDogSaved => _dogs.Any(dog => dog.Id == _newDog.Id);

    private IReadOnlyDictionary<string, string> PositiveDiveraResponses =>
        DiveraResourceSyncService.GetPositiveResponsesByPersonId();

    private int PendingDiveraCheckInCount
    {
        get
        {
            var responses = PositiveDiveraResponses;
            if (responses.Count == 0)
                return 0;

            var checkedInPersonIds = CheckedInResources
                .Where(resource => resource.EntityType == ResourceEntityType.Person)
                .Select(resource => resource.EntityId)
                .ToHashSet(StringComparer.Ordinal);

            return _personnel.Count(person =>
                person.IsActive
                && !checkedInPersonIds.Contains(person.Id)
                && responses.ContainsKey(person.Id));
        }
    }

    private List<ResourceSuggestion> CheckInCandidates
    {
        get
        {
            var query = _searchText.Trim();

            var diveraResponses = PositiveDiveraResponses;
            var checkedInPersonIds = CheckedInResources
                .Where(resource => resource.EntityType == ResourceEntityType.Person)
                .Select(resource => resource.EntityId)
                .ToHashSet(StringComparer.Ordinal);
            var availablePersonnel = _personnel
                .Where(person => person.IsActive && !checkedInPersonIds.Contains(person.Id))
                .ToList();
            var orderedPersonnel = SelectionListOrdering
                .OrderPersonnelByFirstName(availablePersonnel.Where(person => diveraResponses.ContainsKey(person.Id)))
                .Concat(SelectionListOrdering.OrderPersonnelByFirstName(availablePersonnel.Where(person => !diveraResponses.ContainsKey(person.Id))));

            return orderedPersonnel
                .Select(person =>
                {
                    var dogNames = ResourceRosterBuilder.GetActiveDogNamesForHandler(person.Id, _dogs);
                    var dogSummary = dogNames.Count switch
                    {
                        0 => null,
                        1 => $"Hund: {dogNames[0]}",
                        _ => $"Hunde: {string.Join(", ", dogNames)}"
                    };
                    var detail = string.Join(
                        " · ",
                        new[] { person.Organisation, dogSummary }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    diveraResponses.TryGetValue(person.Id, out var diveraResponse);

                    return new ResourceSuggestion(
                        person.Id,
                        person.FullName,
                        string.IsNullOrWhiteSpace(detail) ? null : detail,
                        string.IsNullOrWhiteSpace(diveraResponse) ? null : diveraResponse);
                })
                .Where(suggestion => ResourceCheckInSearch.MatchesQuery(
                    query,
                    suggestion.Name,
                    suggestion.Detail,
                    suggestion.DiveraResponseText))
                .ToList();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _appSettings = await SettingsService.GetAppSettingsAsync();
        _personnel = await MasterDataService.GetPersonalListAsync();
        _dogs = await MasterDataService.GetDogListAsync();
        _checkInExpanded = EinsatzService.GetEinsatzResources().Count == 0;
        SettingsService.AppSettingsChanged += OnAppSettingsChanged;
        EinsatzService.ResourcesChanged += OnResourcesChanged;
        EinsatzService.EinsatzChanged += OnEinsatzChanged;
        DiveraResourceSyncService.ResponsesChanged += OnDiveraResponsesChanged;
        DiveraResourceSyncService.RequestSync();
    }

    public ValueTask DisposeAsync()
    {
        SettingsService.AppSettingsChanged -= OnAppSettingsChanged;
        EinsatzService.ResourcesChanged -= OnResourcesChanged;
        EinsatzService.EinsatzChanged -= OnEinsatzChanged;
        DiveraResourceSyncService.ResponsesChanged -= OnDiveraResponsesChanged;
        return ValueTask.CompletedTask;
    }

    private void OnAppSettingsChanged() => _ = InvokeAsync(async () =>
    {
        _appSettings = await SettingsService.GetAppSettingsAsync();
        StateHasChanged();
    });

    private void OnResourcesChanged() => _ = InvokeAsync(StateHasChanged);
    private void OnEinsatzChanged() => _ = InvokeAsync(StateHasChanged);
    private void OnDiveraResponsesChanged() => _ = InvokeAsync(StateHasChanged);

    private void ToggleCheckIn() => _checkInExpanded = !_checkInExpanded;

    private void ToggleRosterFilters() => _rosterFiltersExpanded = !_rosterFiltersExpanded;

    private void ClearRosterFilters()
    {
        _rosterStateFilter = ResourceRosterFilter.AllResourceStates;
        _rosterHandlerAssignmentFilter = ResourceRosterFilter.AllHandlerAssignments;
        _hideRosterDogs = false;
    }

    private void OpenQuickCreate()
    {
        _showQuickCreate = true;
        _showQuickCreateDog = false;
        _newPerson = new PersonalEntry();
        _newDiveraUserId = string.Empty;
        ClearMessage();
    }

    private void CloseQuickCreate() => _showQuickCreate = false;

    private void OpenDogQuickCreate()
    {
        _showQuickCreate = false;
        _showQuickCreateDog = true;
        _newDog = new DogEntry();
        ClearMessage();
    }

    private void CloseDogQuickCreate()
    {
        _showQuickCreateDog = false;
        if (IsQuickCreateDogSaved)
        {
            SetMessage(
                $"{_newDog.Name} wurde in den Stammdaten gespeichert, aber nicht eingecheckt.",
                isError: true);
        }
    }

    private async Task CheckInPersonAsync(string personId)
    {
        await RunResourceOperationAsync(async () =>
        {
            var name = _personnel.FirstOrDefault(person => person.Id == personId)?.FullName ?? personId;
            var diveraResponse = _initialStatus == ResourceStatus.AufAnfahrt
                && PositiveDiveraResponses.TryGetValue(personId, out var responseText)
                    ? responseText
                    : string.Empty;
            await EinsatzService.CheckInResourceAsync(new EinsatzResource
            {
                EntityType = ResourceEntityType.Person,
                EntityId = personId,
                Status = _initialStatus,
                DiveraRueckmeldung = diveraResponse
            });
            _searchText = string.Empty;
            SetMessage($"{name} wurde mit Status „{ResourceStatusPresentation.Label(_initialStatus)}“ eingecheckt.");
        });
    }

    private async Task CheckInAllDiveraAsync()
    {
        await RunResourceOperationAsync(async () =>
        {
            var checkedInCount = await DiveraResourceSyncService.CheckInPendingPositiveResponsesAsync();
            if (checkedInCount == 0)
            {
                SetMessage("Keine offenen DIVERA-Zusagen zum Einchecken vorhanden.");
                return;
            }

            SetMessage(checkedInCount == 1
                ? "1 Person aus DIVERA wurde mit Status „Auf Anfahrt“ eingecheckt."
                : $"{checkedInCount} Personen aus DIVERA wurden mit Status „Auf Anfahrt“ eingecheckt.");
        });
    }

    private async Task CheckInHandlerDogAsync(ResourceRosterItem handlerItem, UncheckedDogCandidate dog)
    {
        var initialDogStatus = ResourceRosterBuilder.DeriveDogCheckInStatus(handlerItem.Status);
        await RunResourceOperationAsync(async () =>
        {
            await EinsatzService.CheckInResourceAsync(new EinsatzResource
            {
                EntityType = ResourceEntityType.Dog,
                EntityId = dog.DogId,
                Status = initialDogStatus
            });
            SetMessage($"{dog.Name} wurde mit Status „{ResourceStatusPresentation.Label(initialDogStatus)}“ eingecheckt.");
        });
    }

    private async Task CreateAndCheckInAsync()
    {
        if (string.IsNullOrWhiteSpace(_newPerson.Vorname) || string.IsNullOrWhiteSpace(_newPerson.Nachname))
        {
            SetMessage("Vor- und Nachname sind erforderlich.", isError: true);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_newDiveraUserId))
        {
            if (!int.TryParse(_newDiveraUserId, out var diveraUserId) || diveraUserId <= 0)
            {
                SetMessage("Die Divera-Benutzer-ID muss eine positive Zahl sein.", isError: true);
                return;
            }
            _newPerson.DiveraUserId = diveraUserId;
        }

        _isSaving = true;
        try
        {
            await MasterDataService.AddPersonalAsync(_newPerson);
            _personnel.Add(_newPerson);
            await EinsatzService.CheckInResourceAsync(new EinsatzResource
            {
                EntityType = ResourceEntityType.Person,
                EntityId = _newPerson.Id,
                Status = _initialStatus
            });
            if (_newPerson.DiveraUserId.HasValue)
                DiveraResourceSyncService.RequestSync();
            SetMessage($"{_newPerson.FullName} wurde gespeichert und eingecheckt.");

            _showQuickCreate = false;
            _searchText = string.Empty;
        }
        catch (Exception ex)
        {
            SetMessage($"Schnellaufnahme fehlgeschlagen: {ex.Message}", isError: true);
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task CreateAndCheckInDogAsync()
    {
        if (string.IsNullOrWhiteSpace(_newDog.Name) || string.IsNullOrWhiteSpace(_newDog.Rasse))
        {
            SetMessage("Hundename und Rasse sind erforderlich.", isError: true);
            return;
        }

        if (_isSaving)
            return;

        _newDog.Name = _newDog.Name.Trim();
        _newDog.Rasse = _newDog.Rasse.Trim();
        _newDog.Organisation = _newDog.Organisation.Trim();
        _isSaving = true;
        ClearMessage();
        try
        {
            if (!IsQuickCreateDogSaved)
            {
                await MasterDataService.AddDogAsync(_newDog);
                _dogs.Add(_newDog);
            }

            await EinsatzService.CheckInResourceAsync(new EinsatzResource
            {
                EntityType = ResourceEntityType.Dog,
                EntityId = _newDog.Id,
                Status = _initialStatus
            });
            SetMessage($"{_newDog.Name} wurde in den Stammdaten gespeichert und eingecheckt.");
            _showQuickCreateDog = false;
            _searchText = string.Empty;
        }
        catch (Exception ex)
        {
            SetMessage(
                IsQuickCreateDogSaved
                    ? $"{_newDog.Name} ist in den Stammdaten gespeichert, aber der Check-in ist fehlgeschlagen: {ex.Message}"
                    : $"Schnellaufnahme fehlgeschlagen: {ex.Message}",
                isError: true);
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task OnStatusSelectedAsync(ResourceRosterItem item, string? value)
    {
        if (!Enum.TryParse<ResourceStatus>(value, out var status))
            return;

        if (status == ResourceStatus.Sonderauftrag)
        {
            ClearPendingForEntity(item);
            _pendingStatuses[item.UiKey] = status;
            _pendingNotes[item.UiKey] = string.Empty;
            return;
        }

        ClearPendingForEntity(item);
        await RunResourceOperationAsync(async () =>
        {
            await EinsatzService.SetResourceStatusAsync(item.EntityType, item.EntityId, status);
            SetMessage($"{item.Name}: Status auf „{ResourceStatusPresentation.Label(status)}“ geändert.");
        });
    }

    private async Task ConfirmSpecialAssignmentAsync(ResourceRosterItem item)
    {
        var note = PendingNote(item);
        if (string.IsNullOrWhiteSpace(note))
        {
            SetMessage("Für einen Sonderauftrag muss der Zweck eingetragen werden.", isError: true);
            return;
        }

        await RunResourceOperationAsync(async () =>
        {
            await EinsatzService.SetResourceStatusAsync(
                item.EntityType,
                item.EntityId,
                ResourceStatus.Sonderauftrag,
                note);
            ClearPendingForEntity(item);
            SetMessage($"Sonderauftrag für {item.Name} wurde übernommen.");
        });
    }

    private void CancelSpecialAssignment(ResourceRosterItem item)
    {
        _pendingStatuses.Remove(item.UiKey);
        _pendingNotes.Remove(item.UiKey);
        ClearMessage();
    }

    private void RequestResourceRemoval(ResourceRosterItem item)
    {
        _resourceRemovalCandidate = item;
        _resourceRemovalError = string.Empty;
        ClearMessage();
    }

    private void CancelResourceRemoval()
    {
        _resourceRemovalCandidate = null;
        _resourceRemovalError = string.Empty;
    }

    private async Task ConfirmResourceRemovalAsync()
    {
        var candidate = _resourceRemovalCandidate;
        if (candidate is null || _isSaving)
            return;

        _resourceRemovalError = string.Empty;
        if (IsResourceRemovalBlocked(candidate))
        {
            _resourceRemovalError =
                "Diese Ressource ist einem Team oder Suchsektor zugeordnet. Löse zuerst die Teamzuordnung.";
            return;
        }

        _isSaving = true;
        try
        {
            await EinsatzService.RemoveResourceAsync(candidate.EntityType, candidate.EntityId);
            ClearPendingForEntity(candidate);
            _resourceRemovalCandidate = null;
            SetMessage($"{candidate.TypeLabel} {candidate.Name} wurde aus dem Einsatz entfernt. Die Stammdaten bleiben erhalten.");
        }
        catch (Exception ex)
        {
            _resourceRemovalError = $"Entfernen fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void ClearPendingForEntity(ResourceRosterItem item)
    {
        var suffix = item.EntityType == ResourceEntityType.Dog
            ? $"/Dog:{item.EntityId}"
            : null;
        var exactKey = $"{item.EntityType}:{item.EntityId}";
        var matchingKeys = _pendingStatuses.Keys
            .Where(key => string.Equals(key, exactKey, StringComparison.Ordinal)
                || (suffix is not null && key.EndsWith(suffix, StringComparison.Ordinal)))
            .ToArray();

        foreach (var key in matchingKeys)
        {
            _pendingStatuses.Remove(key);
            _pendingNotes.Remove(key);
        }
    }

    private async Task RunResourceOperationAsync(Func<Task> operation)
    {
        _isSaving = true;
        ClearMessage();
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            SetMessage($"Änderung fehlgeschlagen: {ex.Message}", isError: true);
        }
        finally
        {
            _isSaving = false;
        }
    }

    private IEnumerable<ResourceStatus> StatusOptions(ResourceEntityType type) =>
        ResourceStatusPresentation.Options(type);

    private string GetResourceIconClass(ResourceEntityType type) =>
        ResourceIconPresentation.CssClass(type, _appSettings.CollarMarkerIconOrDefault);

    private bool IsSpecialAssignmentPending(ResourceRosterItem item) =>
        _pendingStatuses.ContainsKey(item.UiKey);

    private bool IsResourceRemovalBlocked(ResourceRosterItem item) =>
        EinsatzService.IsResourceInActiveTeam(item.EntityType, item.EntityId);

    private string PendingNote(ResourceRosterItem item) =>
        _pendingNotes.GetValueOrDefault(item.UiKey, string.Empty);

    private void SetPendingNote(ResourceRosterItem item, string? note) =>
        _pendingNotes[item.UiKey] = note ?? string.Empty;

    private void SetMessage(string text, bool isError = false)
    {
        _message = text;
        _messageIsError = isError;
    }

    private void ClearMessage()
    {
        _message = string.Empty;
        _messageIsError = false;
    }

    private sealed record ResourceSuggestion(
        string Id,
        string Name,
        string? Detail,
        string? DiveraResponseText)
    {
        public bool HasDiveraResponse => !string.IsNullOrWhiteSpace(DiveraResponseText);
    }
}

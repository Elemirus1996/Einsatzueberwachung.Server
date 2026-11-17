using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class Ressourcenverwaltung
{
    [Inject] private IEinsatzService EinsatzService { get; set; } = default!;
    [Inject] private IMasterDataService MasterDataService { get; set; } = default!;

    private List<PersonalEntry> _personnel = new();
    private List<DogEntry> _dogs = new();
    private Dictionary<string, ResourceStatus> _pendingStatuses = new(StringComparer.Ordinal);
    private Dictionary<string, string> _pendingNotes = new(StringComparer.Ordinal);
    private ResourceEntityType _searchType = ResourceEntityType.Person;
    private ResourceEntityType _quickCreateType = ResourceEntityType.Person;
    private ResourceStatus _initialStatus = ResourceStatus.AufAnfahrt;
    private string _searchText = string.Empty;
    private string _newDiveraUserId = string.Empty;
    private string _message = string.Empty;
    private bool _messageIsError;
    private bool _showQuickCreate;
    private bool _checkInExpanded;
    private bool _isSaving;
    private PersonalEntry _newPerson = new();
    private DogEntry _newDog = new();

    private bool HasActiveEinsatz =>
        !string.IsNullOrWhiteSpace(EinsatzService.CurrentEinsatz.Einsatzort)
        && EinsatzService.CurrentEinsatz.EinsatzEnde is null;

    private List<EinsatzResource> SortedResources => EinsatzService.GetEinsatzResources()
        .OrderBy(resource => ResourceStatusPresentation.Priority(resource.Status))
        .ThenBy(resource => ResourceName(resource), StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private int InVerfuegungsraumCount => SortedResources.Count(resource => resource.Status == ResourceStatus.Verfuegbar);

    private int ActiveMasterDataCount => _searchType == ResourceEntityType.Person
        ? _personnel.Count(person => person.IsActive)
        : _dogs.Count(dog => dog.IsActive);

    private List<ResourceSuggestion> CheckInCandidates
    {
        get
        {
            var query = _searchText.Trim();
            var checkedIn = EinsatzService.GetEinsatzResources()
                .Where(resource => resource.EntityType == _searchType)
                .Select(resource => resource.EntityId)
                .ToHashSet(StringComparer.Ordinal);

            if (_searchType == ResourceEntityType.Person)
            {
                return _personnel
                    .Where(person => person.IsActive && !checkedIn.Contains(person.Id))
                    .Select(person => new ResourceSuggestion(
                        ResourceEntityType.Person,
                        person.Id,
                        person.FullName,
                        person.Organisation))
                    .Where(suggestion => string.IsNullOrWhiteSpace(query)
                        || ContainsSearchTerm(suggestion.Name, query)
                        || ContainsSearchTerm(suggestion.Detail, query))
                    .OrderBy(suggestion => suggestion.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }

            return _dogs
                .Where(dog => dog.IsActive && !checkedIn.Contains(dog.Id))
                .Select(dog => new ResourceSuggestion(
                    ResourceEntityType.Dog,
                    dog.Id,
                    dog.Name,
                    string.Join(" · ", new[] { dog.Rasse, dog.Organisation }.Where(value => !string.IsNullOrWhiteSpace(value)))))
                .Where(suggestion => string.IsNullOrWhiteSpace(query)
                    || ContainsSearchTerm(suggestion.Name, query)
                    || ContainsSearchTerm(suggestion.Detail, query))
                .OrderBy(suggestion => suggestion.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _personnel = await MasterDataService.GetPersonalListAsync();
        _dogs = await MasterDataService.GetDogListAsync();
        _checkInExpanded = EinsatzService.GetEinsatzResources().Count == 0;
        EinsatzService.ResourcesChanged += OnResourcesChanged;
        EinsatzService.EinsatzChanged += OnEinsatzChanged;
    }

    public ValueTask DisposeAsync()
    {
        EinsatzService.ResourcesChanged -= OnResourcesChanged;
        EinsatzService.EinsatzChanged -= OnEinsatzChanged;
        return ValueTask.CompletedTask;
    }

    private void OnResourcesChanged() => _ = InvokeAsync(StateHasChanged);
    private void OnEinsatzChanged() => _ = InvokeAsync(StateHasChanged);

    private void ToggleCheckIn() => _checkInExpanded = !_checkInExpanded;

    private void SelectSearchType(ResourceEntityType type)
    {
        _searchType = type;
        _searchText = string.Empty;
        _showQuickCreate = false;
        ClearMessage();
    }

    private void OpenQuickCreate(ResourceEntityType type)
    {
        _quickCreateType = type;
        _showQuickCreate = true;
        _newPerson = new PersonalEntry();
        _newDog = new DogEntry();
        _newDiveraUserId = string.Empty;
        ClearMessage();
    }

    private void CloseQuickCreate() => _showQuickCreate = false;

    private async Task CheckInAsync(ResourceEntityType type, string id)
    {
        await RunResourceOperationAsync(async () =>
        {
            var name = type == ResourceEntityType.Person
                ? _personnel.FirstOrDefault(person => person.Id == id)?.FullName ?? id
                : _dogs.FirstOrDefault(dog => dog.Id == id)?.Name ?? id;
            await EinsatzService.CheckInResourceAsync(new EinsatzResource
            {
                EntityType = type,
                EntityId = id,
                Status = _initialStatus
            });
            _searchText = string.Empty;
            SetMessage($"{name} wurde mit Status „{ResourceStatusPresentation.Label(_initialStatus)}“ eingecheckt.");
        });
    }

    private async Task CreateAndCheckInAsync()
    {
        if (_quickCreateType == ResourceEntityType.Person)
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
        }
        else if (string.IsNullOrWhiteSpace(_newDog.Name))
        {
            SetMessage("Der Hundename ist erforderlich.", isError: true);
            return;
        }

        _isSaving = true;
        try
        {
            if (_quickCreateType == ResourceEntityType.Person)
            {
                await MasterDataService.AddPersonalAsync(_newPerson);
                _personnel.Add(_newPerson);
                await EinsatzService.CheckInResourceAsync(new EinsatzResource
                {
                    EntityType = ResourceEntityType.Person,
                    EntityId = _newPerson.Id,
                    Status = _initialStatus
                });
                SetMessage($"{_newPerson.FullName} wurde gespeichert und eingecheckt.");
            }
            else
            {
                await MasterDataService.AddDogAsync(_newDog);
                _dogs.Add(_newDog);
                await EinsatzService.CheckInResourceAsync(new EinsatzResource
                {
                    EntityType = ResourceEntityType.Dog,
                    EntityId = _newDog.Id,
                    Status = _initialStatus
                });
                SetMessage($"{_newDog.Name} wurde gespeichert und eingecheckt.");
            }

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

    private async Task OnStatusSelectedAsync(EinsatzResource resource, string? value)
    {
        if (!Enum.TryParse<ResourceStatus>(value, out var status))
            return;

        if (status == ResourceStatus.Sonderauftrag)
        {
            var key = ResourceKey(resource);
            _pendingStatuses[key] = status;
            _pendingNotes[key] = string.Empty;
            return;
        }

        await RunResourceOperationAsync(async () =>
        {
            await EinsatzService.SetResourceStatusAsync(resource.EntityType, resource.EntityId, status);
            SetMessage($"{ResourceName(resource)}: Status auf „{ResourceStatusPresentation.Label(status)}“ geändert.");
        });
    }

    private async Task ConfirmSpecialAssignmentAsync(EinsatzResource resource)
    {
        var key = ResourceKey(resource);
        var note = PendingNote(resource);
        if (string.IsNullOrWhiteSpace(note))
        {
            SetMessage("Für einen Sonderauftrag muss der Zweck eingetragen werden.", isError: true);
            return;
        }

        await RunResourceOperationAsync(async () =>
        {
            await EinsatzService.SetResourceStatusAsync(
                resource.EntityType,
                resource.EntityId,
                ResourceStatus.Sonderauftrag,
                note);
            _pendingStatuses.Remove(key);
            _pendingNotes.Remove(key);
            SetMessage($"Sonderauftrag für {ResourceName(resource)} wurde übernommen.");
        });
    }

    private void CancelSpecialAssignment(EinsatzResource resource)
    {
        var key = ResourceKey(resource);
        _pendingStatuses.Remove(key);
        _pendingNotes.Remove(key);
        ClearMessage();
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

    private string ResourceName(EinsatzResource resource)
    {
        if (resource.EntityType == ResourceEntityType.Person)
            return _personnel.FirstOrDefault(person => person.Id == resource.EntityId)?.FullName
                ?? $"Unbekannte Person ({resource.EntityId})";
        return _dogs.FirstOrDefault(dog => dog.Id == resource.EntityId)?.Name
            ?? $"Unbekannter Hund ({resource.EntityId})";
    }

    private static string ResourceKey(EinsatzResource resource) => $"{resource.EntityType}:{resource.EntityId}";

    private bool IsSpecialAssignmentPending(EinsatzResource resource) =>
        _pendingStatuses.ContainsKey(ResourceKey(resource));

    private string PendingNote(EinsatzResource resource) =>
        _pendingNotes.GetValueOrDefault(ResourceKey(resource), string.Empty);

    private void SetPendingNote(EinsatzResource resource, string? note) =>
        _pendingNotes[ResourceKey(resource)] = note ?? string.Empty;

    private static bool ContainsSearchTerm(string? value, string term) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(term, StringComparison.CurrentCultureIgnoreCase);

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
        ResourceEntityType EntityType,
        string Id,
        string Name,
        string? Detail);
}

using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Components.Shared;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{
    private const string AllResourceStatesFilter = "all";
    private const string NotAvailableResourceFilter = "not-available";
    private const string AllHandlerAssignmentsFilter = "all";
    private const string HandlersOnlyFilter = "handlers";
    private const string NoAssignedDogFilter = "no-dog";

    private string _resourceStatusError = string.Empty;
    private string? _pendingSpecialResourceKey;
    private string _pendingSpecialResourceNote = string.Empty;
    private string _resourceStateFilter = AllResourceStatesFilter;
    private string _handlerAssignmentFilter = AllHandlerAssignmentsFilter;
    private bool _isUpdatingResourceStatus;
    private bool _resourceFiltersExpanded;
    private bool _hideDogs;

    private void OnResourcesChanged() => _ = InvokeAsync(StateHasChanged);

    private void ToggleResourceFilters() => _resourceFiltersExpanded = !_resourceFiltersExpanded;

    private string GetResourceIconClass(ResourceRosterItem item) =>
        ResourceIconPresentation.CssClass(item.EntityType, _appSettings.CollarMarkerIconOrDefault);

    private int GetActiveResourceFilterCount() =>
        (_resourceStateFilter == AllResourceStatesFilter ? 0 : 1)
        + (_handlerAssignmentFilter == AllHandlerAssignmentsFilter ? 0 : 1)
        + (_hideDogs ? 1 : 0);

    private void ClearResourceFilters()
    {
        _resourceStateFilter = AllResourceStatesFilter;
        _handlerAssignmentFilter = AllHandlerAssignmentsFilter;
        _hideDogs = false;
    }

    private IReadOnlyList<FilteredResourceGroup> GetFilteredMonitorResourceGroups()
    {
        var groups = GetMonitorResourceGroups();
        var filteredGroups = new List<FilteredResourceGroup>();

        foreach (var group in groups)
        {
            if (_hideDogs && group.Primary.EntityType == ResourceEntityType.Dog)
                continue;

            if (!MatchesHandlerAssignmentFilter(group))
                continue;

            var primaryMatches = MatchesResourceStateFilter(group.Primary);
            var matchingDogs = _hideDogs
                ? Array.Empty<ResourceRosterItem>()
                : primaryMatches
                    ? group.CheckedInDogs
                    : group.CheckedInDogs.Where(MatchesResourceStateFilter).ToArray();

            if (group.Primary.EntityType == ResourceEntityType.Person)
            {
                if (!primaryMatches && matchingDogs.Count == 0)
                    continue;

                filteredGroups.Add(new FilteredResourceGroup(
                    group.Primary,
                    primaryMatches,
                    matchingDogs));
                continue;
            }

            if (primaryMatches)
                filteredGroups.Add(new FilteredResourceGroup(group.Primary, true, Array.Empty<ResourceRosterItem>()));
        }

        return filteredGroups;
    }

    private bool MatchesResourceStateFilter(ResourceRosterItem item) =>
        _resourceStateFilter switch
        {
            AllResourceStatesFilter => true,
            NotAvailableResourceFilter => item.Status != ResourceStatus.Verfuegbar,
            _ when Enum.TryParse<ResourceStatus>(_resourceStateFilter, out var status) => item.Status == status,
            _ => true
        };

    private bool MatchesHandlerAssignmentFilter(ResourceRosterGroup group)
    {
        if (group.Primary.EntityType != ResourceEntityType.Person)
            return _handlerAssignmentFilter == AllHandlerAssignmentsFilter;

        var hasAssignedDog = _dogList.Any(dog =>
            dog.HundefuehrerIds?.Contains(group.Primary.EntityId, StringComparer.Ordinal) == true);

        return _handlerAssignmentFilter switch
        {
            HandlersOnlyFilter => hasAssignedDog,
            NoAssignedDogFilter => !hasAssignedDog,
            _ => true
        };
    }

    private sealed record FilteredResourceGroup(
        ResourceRosterItem Primary,
        bool ShowPrimaryStatus,
        IReadOnlyList<ResourceRosterItem> Dogs);

    private async Task OnResourceStatusSelectedAsync(ResourceRosterItem item, string? value)
    {
        if (!Enum.TryParse<ResourceStatus>(value, out var status)
            || !ResourceStatusPresentation.Options(item.EntityType).Contains(status))
        {
            _resourceStatusError = "Der ausgewählte Ressourcenstatus ist ungültig.";
            return;
        }

        _resourceStatusError = string.Empty;
        if (status == ResourceStatus.Sonderauftrag)
        {
            _pendingSpecialResourceKey = item.UiKey;
            _pendingSpecialResourceNote = string.Empty;
            return;
        }

        CancelSpecialResourceAssignment();
        await SetResourceStatusAsync(item, status);
    }

    private void SetPendingSpecialResourceNote(string? note)
    {
        _pendingSpecialResourceNote = note ?? string.Empty;
        _resourceStatusError = string.Empty;
    }

    private async Task ConfirmSpecialResourceAssignmentAsync(ResourceRosterItem item)
    {
        if (string.IsNullOrWhiteSpace(_pendingSpecialResourceNote))
        {
            _resourceStatusError = "Für einen Sonderauftrag muss der Zweck eingetragen werden.";
            return;
        }

        await SetResourceStatusAsync(item, ResourceStatus.Sonderauftrag, _pendingSpecialResourceNote);
    }

    private void CancelSpecialResourceAssignment()
    {
        _pendingSpecialResourceKey = null;
        _pendingSpecialResourceNote = string.Empty;
        _resourceStatusError = string.Empty;
    }

    private async Task SetResourceStatusAsync(
        ResourceRosterItem item,
        ResourceStatus status,
        string? note = null)
    {
        if (_isUpdatingResourceStatus)
            return;

        _isUpdatingResourceStatus = true;
        _resourceStatusError = string.Empty;
        try
        {
            await EinsatzService.SetResourceStatusAsync(item.EntityType, item.EntityId, status, note);
            CancelSpecialResourceAssignment();
        }
        catch (Exception ex)
        {
            _resourceStatusError = $"Status für {item.Name} konnte nicht geändert werden: {ex.Message}";
        }
        finally
        {
            _isUpdatingResourceStatus = false;
        }
    }

    private IReadOnlyList<ResourceRosterGroup> GetMonitorResourceGroups() =>
        ResourceRosterBuilder.Build(EinsatzService.GetEinsatzResources(), _personalList, _dogList);
}

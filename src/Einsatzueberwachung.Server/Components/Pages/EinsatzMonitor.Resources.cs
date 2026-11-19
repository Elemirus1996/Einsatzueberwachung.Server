using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Components.Shared;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{
    private string _resourceStatusError = string.Empty;
    private string? _pendingSpecialResourceKey;
    private string _pendingSpecialResourceNote = string.Empty;
    private string _resourceStateFilter = ResourceRosterFilter.AllResourceStates;
    private string _handlerAssignmentFilter = ResourceRosterFilter.AllHandlerAssignments;
    private bool _isUpdatingResourceStatus;
    private bool _resourceFiltersExpanded;
    private bool _hideDogs;

    private void OnResourcesChanged() => _ = InvokeAsync(StateHasChanged);

    private void ToggleResourceFilters() => _resourceFiltersExpanded = !_resourceFiltersExpanded;

    private string GetResourceIconClass(ResourceRosterItem item) =>
        ResourceIconPresentation.CssClass(item.EntityType, _appSettings.CollarMarkerIconOrDefault);

    private int GetActiveResourceFilterCount() =>
        ResourceRosterFilter.CountActiveFilters(_resourceStateFilter, _handlerAssignmentFilter, _hideDogs);

    private void ClearResourceFilters()
    {
        _resourceStateFilter = ResourceRosterFilter.AllResourceStates;
        _handlerAssignmentFilter = ResourceRosterFilter.AllHandlerAssignments;
        _hideDogs = false;
    }

    private IReadOnlyList<FilteredResourceGroup> GetFilteredMonitorResourceGroups()
        => ResourceRosterFilter.Apply(
            GetMonitorResourceGroups(),
            _dogList,
            _resourceStateFilter,
            _handlerAssignmentFilter,
            _hideDogs);

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

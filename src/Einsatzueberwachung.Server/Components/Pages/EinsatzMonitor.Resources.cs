using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Components.Shared;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{
    private string _resourceStatusError = string.Empty;
    private string? _pendingSpecialResourceKey;
    private string _pendingSpecialResourceNote = string.Empty;
    private bool _isUpdatingResourceStatus;

    private void OnResourcesChanged() => _ = InvokeAsync(StateHasChanged);

    private int GetAvailableResourceCount() =>
        EinsatzService.GetEinsatzResources().Count(resource => resource.Status == ResourceStatus.Verfuegbar);

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

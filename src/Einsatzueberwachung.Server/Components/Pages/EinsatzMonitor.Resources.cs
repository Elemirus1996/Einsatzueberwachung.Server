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

    private async Task OnResourceStatusSelectedAsync(ResourcePanelItem row, string? value)
    {
        if (!Enum.TryParse<ResourceStatus>(value, out var status)
            || !ResourceStatusPresentation.Options(row.EntityType).Contains(status))
        {
            _resourceStatusError = "Der ausgewählte Ressourcenstatus ist ungültig.";
            return;
        }

        _resourceStatusError = string.Empty;
        if (status == ResourceStatus.Sonderauftrag)
        {
            _pendingSpecialResourceKey = row.Key;
            _pendingSpecialResourceNote = string.Empty;
            return;
        }

        CancelSpecialResourceAssignment();
        await SetResourceStatusAsync(row, status);
    }

    private void SetPendingSpecialResourceNote(string? note)
    {
        _pendingSpecialResourceNote = note ?? string.Empty;
        _resourceStatusError = string.Empty;
    }

    private async Task ConfirmSpecialResourceAssignmentAsync(ResourcePanelItem row)
    {
        if (string.IsNullOrWhiteSpace(_pendingSpecialResourceNote))
        {
            _resourceStatusError = "Für einen Sonderauftrag muss der Zweck eingetragen werden.";
            return;
        }

        await SetResourceStatusAsync(row, ResourceStatus.Sonderauftrag, _pendingSpecialResourceNote);
    }

    private void CancelSpecialResourceAssignment()
    {
        _pendingSpecialResourceKey = null;
        _pendingSpecialResourceNote = string.Empty;
        _resourceStatusError = string.Empty;
    }

    private async Task SetResourceStatusAsync(
        ResourcePanelItem row,
        ResourceStatus status,
        string? note = null)
    {
        if (_isUpdatingResourceStatus)
            return;

        _isUpdatingResourceStatus = true;
        _resourceStatusError = string.Empty;
        try
        {
            await EinsatzService.SetResourceStatusAsync(row.EntityType, row.EntityId, status, note);
            CancelSpecialResourceAssignment();
        }
        catch (Exception ex)
        {
            _resourceStatusError = $"Status für {row.Name} konnte nicht geändert werden: {ex.Message}";
        }
        finally
        {
            _isUpdatingResourceStatus = false;
        }
    }

    private List<ResourcePanelItem> GetMonitorResources()
    {
        var resources = EinsatzService.GetEinsatzResources();
        return resources
            .Select(resource => new ResourcePanelItem(
                $"{resource.EntityType}:{resource.EntityId}",
                resource.EntityType,
                resource.EntityId,
                resource.EntityType == ResourceEntityType.Person
                    ? _personalList.FirstOrDefault(person => person.Id == resource.EntityId)?.FullName ?? $"Unbekannte Person ({resource.EntityId})"
                    : _dogList.FirstOrDefault(dog => dog.Id == resource.EntityId)?.Name ?? $"Unbekannter Hund ({resource.EntityId})",
                resource.EntityType == ResourceEntityType.Person ? "Person" : "Hund",
                resource.Status,
                resource.Status == ResourceStatus.Sonderauftrag
                    ? resource.SonderauftragNotiz
                    : resource.Status == ResourceStatus.AufAnfahrt
                        ? resource.DiveraRueckmeldung
                        : string.Empty))
            .OrderBy(row => ResourceStatusPresentation.Priority(row.Status))
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private sealed record ResourcePanelItem(
        string Key,
        ResourceEntityType EntityType,
        string EntityId,
        string Name,
        string TypeLabel,
        ResourceStatus Status,
        string Note);
}

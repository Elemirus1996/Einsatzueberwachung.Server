using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Components.Shared;

namespace Einsatzueberwachung.Server.Components.Pages;

public partial class EinsatzMonitor
{
    private void OnResourcesChanged() => _ = InvokeAsync(StateHasChanged);

    private int GetAvailableResourceCount() =>
        EinsatzService.GetEinsatzResources().Count(resource => resource.Status == ResourceStatus.Verfuegbar);

    private List<ResourcePanelItem> GetMonitorResources()
    {
        var resources = EinsatzService.GetEinsatzResources();
        return resources
            .Select(resource => new ResourcePanelItem(
                $"{resource.EntityType}:{resource.EntityId}",
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
        string Name,
        string TypeLabel,
        ResourceStatus Status,
        string Note);
}

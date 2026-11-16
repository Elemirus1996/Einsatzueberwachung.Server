using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Services;

public partial class EinsatzService
{
    public IReadOnlyList<EinsatzResource> GetEinsatzResources()
    {
        RefreshExpiredDogResourcePauses();
        return (CurrentEinsatz.Resources ??= new List<EinsatzResource>())
            .Select(CloneResource)
            .ToArray();
    }

    public Task CheckInResourceAsync(EinsatzResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!CurrentEinsatz.IstEinsatz)
            throw new InvalidOperationException("Ressourcen können nur in einen laufenden Einsatz eingecheckt werden.");
        if (string.IsNullOrWhiteSpace(resource.EntityId))
            throw new ArgumentException("Die Ressourcen-ID darf nicht leer sein.", nameof(resource));
        if (resource.Status is not (ResourceStatus.Verfuegbar or ResourceStatus.AufAnfahrt))
            throw new ArgumentException("Beim Check-in sind nur die Status Verfügbar und Auf Anfahrt zulässig.", nameof(resource));

        var resources = CurrentEinsatz.Resources ??= new List<EinsatzResource>();
        var existing = resources.FirstOrDefault(entry =>
            entry.EntityType == resource.EntityType
            && string.Equals(entry.EntityId, resource.EntityId, StringComparison.Ordinal));

        if (existing is not null)
        {
            if (resource.EntityType == ResourceEntityType.Person
                && existing.Status == ResourceStatus.AufAnfahrt
                && !string.IsNullOrWhiteSpace(resource.DiveraRueckmeldung)
                && !string.Equals(existing.DiveraRueckmeldung, resource.DiveraRueckmeldung, StringComparison.Ordinal))
            {
                existing.DiveraRueckmeldung = resource.DiveraRueckmeldung;
                existing.StatusChangedAt = Ortszeit.Jetzt;
                ResourcesChanged?.Invoke();
            }

            return Task.CompletedTask;
        }

        var now = Ortszeit.Jetzt;
        resources.Add(new EinsatzResource
        {
            EntityType = resource.EntityType,
            EntityId = resource.EntityId,
            Status = resource.Status,
            SonderauftragNotiz = string.Empty,
            DiveraRueckmeldung = resource.EntityType == ResourceEntityType.Person
                ? resource.DiveraRueckmeldung
                : string.Empty,
            CheckedInAt = now,
            StatusChangedAt = now
        });
        ResourcesChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task SetResourceStatusAsync(
        ResourceEntityType entityType,
        string entityId,
        ResourceStatus status,
        string? note = null)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            throw new ArgumentException("Die Ressourcen-ID darf nicht leer sein.", nameof(entityId));
        if (status == ResourceStatus.InSuche)
            throw new ArgumentException("Der Status In Suche wird ausschließlich durch die Suchzuweisung gesetzt.", nameof(status));
        if (status == ResourceStatus.Sonderauftrag && string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Für einen Sonderauftrag ist eine Notiz erforderlich.", nameof(note));

        var resource = (CurrentEinsatz.Resources ??= new List<EinsatzResource>())
            .FirstOrDefault(entry =>
                entry.EntityType == entityType
                && string.Equals(entry.EntityId, entityId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Die Ressource ist nicht in den Einsatz eingecheckt.");

        var normalizedNote = status == ResourceStatus.Sonderauftrag ? note!.Trim() : string.Empty;
        if (resource.Status == status && resource.SonderauftragNotiz == normalizedNote)
            return Task.CompletedTask;

        resource.Status = status;
        resource.SonderauftragNotiz = normalizedNote;
        resource.DiveraRueckmeldung = string.Empty;
        resource.StatusChangedAt = Ortszeit.Jetzt;
        ResourcesChanged?.Invoke();
        return Task.CompletedTask;
    }

    public bool CanAssignResourceToSearch(ResourceEntityType entityType, string entityId)
    {
        RefreshExpiredDogResourcePauses();
        return (CurrentEinsatz.Resources ??= new List<EinsatzResource>())
            .Any(resource =>
                resource.EntityType == entityType
                && string.Equals(resource.EntityId, entityId, StringComparison.Ordinal)
                && resource.Status == ResourceStatus.Verfuegbar);
    }

    public void RefreshExpiredDogResourcePauses()
    {
        if (_dogPauses.Count == 0)
            return;

        var now = Ortszeit.Jetzt;
        var changed = false;
        foreach (var (dogId, pause) in _dogPauses.ToArray())
        {
            if (!pause.IsPauseComplete)
                continue;

            var resource = (CurrentEinsatz.Resources ??= new List<EinsatzResource>())
                .FirstOrDefault(entry =>
                    entry.EntityType == ResourceEntityType.Dog
                    && string.Equals(entry.EntityId, dogId, StringComparison.Ordinal));
            if (resource?.Status != ResourceStatus.Pause)
                continue;

            resource.Status = ResourceStatus.Verfuegbar;
            resource.StatusChangedAt = now;
            changed = true;
        }

        if (changed)
            ResourcesChanged?.Invoke();
    }

    private void SetResourceStatusFromSearch(ResourceEntityType entityType, string? entityId, ResourceStatus status)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return;

        var resource = (CurrentEinsatz.Resources ??= new List<EinsatzResource>())
            .FirstOrDefault(entry =>
                entry.EntityType == entityType
                && string.Equals(entry.EntityId, entityId, StringComparison.Ordinal));
        if (resource is null || resource.Status == status)
            return;

        resource.Status = status;
        resource.SonderauftragNotiz = string.Empty;
        resource.DiveraRueckmeldung = string.Empty;
        resource.StatusChangedAt = Ortszeit.Jetzt;
        ResourcesChanged?.Invoke();
    }

    private static EinsatzResource CloneResource(EinsatzResource resource) => new()
    {
        EntityType = resource.EntityType,
        EntityId = resource.EntityId,
        Status = resource.Status,
        SonderauftragNotiz = resource.SonderauftragNotiz,
        DiveraRueckmeldung = resource.DiveraRueckmeldung,
        CheckedInAt = resource.CheckedInAt,
        StatusChangedAt = resource.StatusChangedAt
    };

    private static IEnumerable<string> GetTeamPersonIds(Team team)
    {
        if (!string.IsNullOrWhiteSpace(team.HundefuehrerId))
            yield return team.HundefuehrerId;
        foreach (var helperId in (team.HelferIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)))
            yield return helperId;
    }

    private void EnsureTeamResourcesAvailable(Team team, bool allowAssignedInSearch = false)
    {
        var resources = CurrentEinsatz.Resources ??= new List<EinsatzResource>();
        var alreadyAssignedToSearch = allowAssignedInSearch
            && !string.IsNullOrWhiteSpace(team.SearchAreaId)
            && CurrentEinsatz.SearchAreas.Any(area =>
                area.Id == team.SearchAreaId && area.AssignedTeamId == team.TeamId);
        var required = GetTeamPersonIds(team)
            .Select(id => (ResourceEntityType.Person, Id: id))
            .Concat(!string.IsNullOrWhiteSpace(team.DogId)
                ? new[] { (ResourceEntityType.Dog, Id: team.DogId) }
                : Array.Empty<(ResourceEntityType EntityType, string Id)>());
        var requiredResources = required.ToList();
        if (requiredResources.Count == 0)
            return;

        foreach (var (entityType, id) in requiredResources)
        {
            var resource = resources.FirstOrDefault(entry =>
                entry.EntityType == entityType
                && string.Equals(entry.EntityId, id, StringComparison.Ordinal));
            if (resource?.Status != ResourceStatus.Verfuegbar
                && !(alreadyAssignedToSearch && resource?.Status == ResourceStatus.InSuche))
                throw new InvalidOperationException($"Die Ressource '{id}' ist nicht eingecheckt und verfügbar.");
        }
    }
}

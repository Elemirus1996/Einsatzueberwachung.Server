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

    public IReadOnlyList<TeamResourceAvailabilityIssue> GetTeamStartResourceIssues(string teamId)
    {
        var team = _teams.FirstOrDefault(entry => entry.TeamId == teamId);
        if (team is null)
            return Array.Empty<TeamResourceAvailabilityIssue>();

        RefreshExpiredDogResourcePauses();
        return GetTeamStartResourceIssues(team, allowAssignedInSearch: true);
    }

    public Task CheckInResourceAsync(EinsatzResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!CurrentEinsatz.IstEinsatz)
            throw new InvalidOperationException("Ressourcen können nur in einen laufenden Einsatz eingecheckt werden.");
        if (string.IsNullOrWhiteSpace(resource.EntityId))
            throw new ArgumentException("Die Ressourcen-ID darf nicht leer sein.", nameof(resource));
        if (resource.Status is not (ResourceStatus.Verfuegbar or ResourceStatus.AufAnfahrt))
            throw new ArgumentException("Beim Check-in sind nur die Status Im Verfügungsraum und Auf Anfahrt zulässig.", nameof(resource));

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

    public bool IsResourceInActiveTeam(ResourceEntityType entityType, string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return false;

        return _teams.Any(team =>
            (team.IsRunning
                || team.IsManuallyPaused
                || team.IsPausing
                || !string.IsNullOrWhiteSpace(team.SearchAreaId))
            && (entityType switch
            {
                ResourceEntityType.Person => GetTeamPersonIds(team).Contains(entityId, StringComparer.Ordinal),
                ResourceEntityType.Dog => string.Equals(team.DogId, entityId, StringComparison.Ordinal),
                _ => false
            }));
    }

    public Task RemoveResourceAsync(ResourceEntityType entityType, string entityId)
    {
        if (!CurrentEinsatz.IstEinsatz)
            throw new InvalidOperationException("Ressourcen können nur aus einem laufenden Einsatz entfernt werden.");
        if (string.IsNullOrWhiteSpace(entityId))
            throw new ArgumentException("Die Ressourcen-ID darf nicht leer sein.", nameof(entityId));

        var resources = CurrentEinsatz.Resources ??= new List<EinsatzResource>();
        var resource = resources.FirstOrDefault(entry =>
            entry.EntityType == entityType
            && string.Equals(entry.EntityId, entityId, StringComparison.Ordinal));
        if (resource is null)
            throw new InvalidOperationException("Die Ressource ist nicht in den Einsatz eingecheckt.");
        if (IsResourceInActiveTeam(entityType, entityId))
            throw new InvalidOperationException(
                "Die Ressource ist einem Team oder Suchsektor zugeordnet und kann erst nach dem Lösen der Zuordnung entfernt werden.");

        resources.Remove(resource);
        if (entityType == ResourceEntityType.Dog)
            _dogPauses.Remove(entityId);
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
        var resources = CurrentEinsatz.Resources ??= new List<EinsatzResource>();
        if (resources.Count == 0)
            return true;

        return resources.Any(resource =>
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
        var unavailableResources = GetTeamStartResourceIssues(team, allowAssignedInSearch);
        if (unavailableResources.Count > 0)
            throw new TeamResourceAvailabilityException(unavailableResources);
    }

    private IReadOnlyList<TeamResourceAvailabilityIssue> GetTeamStartResourceIssues(
        Team team,
        bool allowAssignedInSearch)
    {
        var resources = CurrentEinsatz.Resources ??= new List<EinsatzResource>();
        if (resources.Count == 0)
            return Array.Empty<TeamResourceAvailabilityIssue>();

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
            return Array.Empty<TeamResourceAvailabilityIssue>();

        var unavailableResources = new List<TeamResourceAvailabilityIssue>();
        foreach (var (entityType, id) in requiredResources)
        {
            var resource = resources.FirstOrDefault(entry =>
                entry.EntityType == entityType
                && string.Equals(entry.EntityId, id, StringComparison.Ordinal));
            if (resource?.Status != ResourceStatus.Verfuegbar
                && !(alreadyAssignedToSearch && resource?.Status == ResourceStatus.InSuche))
            {
                unavailableResources.Add(new TeamResourceAvailabilityIssue(entityType, id, resource?.Status));
            }
        }

        return unavailableResources;
    }
}

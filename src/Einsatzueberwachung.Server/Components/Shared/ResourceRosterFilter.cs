using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Components.Shared;

public sealed record FilteredResourceGroup(
    ResourceRosterItem Primary,
    bool ShowPrimaryStatus,
    IReadOnlyList<ResourceRosterItem> Dogs,
    IReadOnlyList<UncheckedDogCandidate> UncheckedActiveDogs);

public static class ResourceRosterFilter
{
    public const string AllResourceStates = "all";
    public const string NotAvailable = "not-available";
    public const string AllHandlerAssignments = "all";
    public const string HandlersOnly = "handlers";
    public const string NoAssignedDog = "no-dog";

    public static IReadOnlyList<FilteredResourceGroup> Apply(
        IReadOnlyList<ResourceRosterGroup> groups,
        IReadOnlyList<DogEntry> dogs,
        string stateFilter,
        string handlerAssignmentFilter,
        bool hideDogs)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(dogs);

        var filteredGroups = new List<FilteredResourceGroup>();
        foreach (var group in groups)
        {
            if (hideDogs && group.Primary.EntityType == ResourceEntityType.Dog)
                continue;

            if (!MatchesHandlerAssignmentFilter(group, dogs, handlerAssignmentFilter))
                continue;

            IReadOnlyList<ResourceRosterItem> matchingDogs = hideDogs
                ? Array.Empty<ResourceRosterItem>()
                : group.CheckedInDogs;

            if (group.Primary.EntityType == ResourceEntityType.Person)
            {
                if (!MatchesResourceStateFilter(group.Primary, stateFilter))
                    continue;

                filteredGroups.Add(new FilteredResourceGroup(
                    group.Primary,
                    true,
                    matchingDogs,
                    group.UncheckedActiveDogs));
                continue;
            }

            filteredGroups.Add(new FilteredResourceGroup(
                group.Primary,
                true,
                Array.Empty<ResourceRosterItem>(),
                group.UncheckedActiveDogs));
        }

        return filteredGroups;
    }

    public static int CountActiveFilters(string stateFilter, string handlerAssignmentFilter, bool hideDogs) =>
        (stateFilter == AllResourceStates ? 0 : 1)
        + (handlerAssignmentFilter == AllHandlerAssignments ? 0 : 1)
        + (hideDogs ? 1 : 0);

    private static bool MatchesResourceStateFilter(ResourceRosterItem item, string stateFilter) =>
        stateFilter switch
        {
            AllResourceStates => true,
            NotAvailable => item.Status != ResourceStatus.Verfuegbar,
            _ when Enum.TryParse<ResourceStatus>(stateFilter, out var status) => item.Status == status,
            _ => true
        };

    private static bool MatchesHandlerAssignmentFilter(
        ResourceRosterGroup group,
        IReadOnlyList<DogEntry> dogs,
        string handlerAssignmentFilter)
    {
        if (group.Primary.EntityType != ResourceEntityType.Person)
            return true;

        var hasAssignedDog = dogs.Any(dog =>
            dog.HundefuehrerIds?.Contains(group.Primary.EntityId, StringComparer.Ordinal) == true);

        return handlerAssignmentFilter switch
        {
            HandlersOnly => hasAssignedDog,
            NoAssignedDog => !hasAssignedDog,
            _ => true
        };
    }
}

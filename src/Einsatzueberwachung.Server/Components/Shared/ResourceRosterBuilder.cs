using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Components.Shared;

public sealed record UncheckedDogCandidate(
    string DogId,
    string Name);

public sealed record ResourceRosterItem(
    string UiKey,
    EinsatzResource Resource,
    string Name,
    string TypeLabel)
{
    public ResourceEntityType EntityType => Resource.EntityType;
    public string EntityId => Resource.EntityId;
    public ResourceStatus Status => Resource.Status;

    public string GetMonitorNote() =>
        Resource.Status == ResourceStatus.Sonderauftrag
            ? Resource.SonderauftragNotiz ?? string.Empty
            : Resource.Status == ResourceStatus.AufAnfahrt
                ? Resource.DiveraRueckmeldung ?? string.Empty
                : string.Empty;
}

public sealed record ResourceRosterGroup(
    ResourceRosterItem Primary,
    IReadOnlyList<ResourceRosterItem> CheckedInDogs,
    IReadOnlyList<UncheckedDogCandidate> UncheckedActiveDogs);

public static class ResourceRosterBuilder
{
    public static ResourceStatus DeriveDogCheckInStatus(ResourceStatus handlerStatus) =>
        handlerStatus == ResourceStatus.AufAnfahrt
            ? ResourceStatus.AufAnfahrt
            : ResourceStatus.Verfuegbar;

    public static IReadOnlyList<string> GetActiveDogNamesForHandler(
        string personId,
        IEnumerable<DogEntry> dogs)
    {
        if (string.IsNullOrWhiteSpace(personId))
            return Array.Empty<string>();

        return dogs
            .Where(dog => dog.IsActive
                && dog.HundefuehrerIds is not null
                && dog.HundefuehrerIds.Contains(personId, StringComparer.Ordinal))
            .Select(dog => dog.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ResourceRosterGroup> Build(
        IReadOnlyList<EinsatzResource> resources,
        IReadOnlyList<PersonalEntry> personnel,
        IReadOnlyList<DogEntry> dogs)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(personnel);
        ArgumentNullException.ThrowIfNull(dogs);

        if (resources.Count == 0)
            return Array.Empty<ResourceRosterGroup>();

        var personById = personnel
            .Where(person => !string.IsNullOrWhiteSpace(person.Id))
            .GroupBy(person => person.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var dogById = dogs
            .Where(dog => !string.IsNullOrWhiteSpace(dog.Id))
            .GroupBy(dog => dog.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var checkedInDogsById = resources
            .Where(resource => resource.EntityType == ResourceEntityType.Dog && !string.IsNullOrWhiteSpace(resource.EntityId))
            .GroupBy(resource => resource.EntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var attachedDogIds = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<ResourceRosterGroup>();

        foreach (var personResource in resources.Where(resource => resource.EntityType == ResourceEntityType.Person))
        {
            var personId = personResource.EntityId;
            var personName = personById.TryGetValue(personId, out var person) && !string.IsNullOrWhiteSpace(person.FullName)
                ? person.FullName
                : $"Unbekannte Person ({personId})";

            var associatedDogs = dogs
                .Where(dog => dog.HundefuehrerIds is not null
                    && dog.HundefuehrerIds.Contains(personId, StringComparer.Ordinal))
                .ToList();

            var checkedInHandlerDogs = new List<ResourceRosterItem>();
            var uncheckedHandlerDogs = new List<UncheckedDogCandidate>();

            foreach (var dog in associatedDogs)
            {
                if (checkedInDogsById.TryGetValue(dog.Id, out var dogResource))
                {
                    attachedDogIds.Add(dog.Id);
                    var dogName = !string.IsNullOrWhiteSpace(dog.Name)
                        ? dog.Name
                        : $"Unbekannter Hund ({dog.Id})";
                    checkedInHandlerDogs.Add(new ResourceRosterItem(
                        $"Person:{personId}/Dog:{dog.Id}",
                        dogResource,
                        dogName,
                        "Hund"));
                }
                else if (dog.IsActive)
                {
                    var dogName = !string.IsNullOrWhiteSpace(dog.Name)
                        ? dog.Name
                        : $"Unbekannter Hund ({dog.Id})";
                    uncheckedHandlerDogs.Add(new UncheckedDogCandidate(dog.Id, dogName));
                }
            }

            var sortedCheckedInDogs = checkedInHandlerDogs
                .OrderBy(item => ResourceStatusPresentation.Priority(item.Status))
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.EntityId, StringComparer.Ordinal)
                .ToArray();

            var sortedUncheckedDogs = uncheckedHandlerDogs
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.DogId, StringComparer.Ordinal)
                .ToArray();

            groups.Add(new ResourceRosterGroup(
                new ResourceRosterItem(
                    $"Person:{personId}",
                    personResource,
                    personName,
                    "Person"),
                sortedCheckedInDogs,
                sortedUncheckedDogs));
        }

        foreach (var dogResource in resources.Where(resource =>
                     resource.EntityType == ResourceEntityType.Dog
                     && !attachedDogIds.Contains(resource.EntityId)))
        {
            var dogId = dogResource.EntityId;
            var dogName = dogById.TryGetValue(dogId, out var dog) && !string.IsNullOrWhiteSpace(dog.Name)
                ? dog.Name
                : $"Unbekannter Hund ({dogId})";

            groups.Add(new ResourceRosterGroup(
                new ResourceRosterItem(
                    $"Dog:{dogId}",
                    dogResource,
                    dogName,
                    "Hund"),
                Array.Empty<ResourceRosterItem>(),
                Array.Empty<UncheckedDogCandidate>()));
        }

        return groups
            .OrderBy(group => ResourceStatusPresentation.Priority(group.Primary.Status))
            .ThenBy(group => group.Primary.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group => group.Primary.EntityId, StringComparer.Ordinal)
            .ToArray();
    }
}

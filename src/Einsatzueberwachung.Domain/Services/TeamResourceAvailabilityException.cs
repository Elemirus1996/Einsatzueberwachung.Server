using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services;

public sealed class TeamResourceAvailabilityException : InvalidOperationException
{
    public TeamResourceAvailabilityException(IReadOnlyList<TeamResourceAvailabilityIssue> unavailableResources)
        : base(CreateMessage(unavailableResources))
    {
        UnavailableResources = unavailableResources.ToArray();
    }

    public IReadOnlyList<TeamResourceAvailabilityIssue> UnavailableResources { get; }

    private static string CreateMessage(IReadOnlyList<TeamResourceAvailabilityIssue> unavailableResources)
    {
        ArgumentNullException.ThrowIfNull(unavailableResources);
        if (unavailableResources.Count == 0)
            throw new ArgumentException("Mindestens eine nicht verfügbare Ressource ist erforderlich.", nameof(unavailableResources));

        return $"Die Ressource '{unavailableResources[0].EntityId}' ist nicht im Verfügungsraum und kann nicht zugewiesen werden.";
    }
}

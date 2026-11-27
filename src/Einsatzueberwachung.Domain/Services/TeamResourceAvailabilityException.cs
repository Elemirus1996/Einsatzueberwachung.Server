using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Services;

public sealed class TeamResourceAvailabilityException : InvalidOperationException
{
    public TeamResourceAvailabilityException(
        IReadOnlyList<TeamResourceAvailabilityIssue> unavailableResources,
        Team? team = null)
        : base(CreateMessage(unavailableResources, team))
    {
        UnavailableResources = unavailableResources.ToArray();
    }

    public IReadOnlyList<TeamResourceAvailabilityIssue> UnavailableResources { get; }

    private static string CreateMessage(
        IReadOnlyList<TeamResourceAvailabilityIssue> unavailableResources,
        Team? team)
    {
        ArgumentNullException.ThrowIfNull(unavailableResources);
        if (unavailableResources.Count == 0)
            throw new ArgumentException("Mindestens eine nicht verfügbare Ressource ist erforderlich.", nameof(unavailableResources));

        var first = unavailableResources[0];
        var displayName = ResolveDisplayName(first, team);
        var statusDetail = first.Status is { } status
            ? $"Status: {FormatStatusLabel(status)}"
            : "nicht eingecheckt";

        return unavailableResources.Count == 1
            ? $"{displayName} ({statusDetail}) ist nicht im Verfügungsraum."
            : $"{displayName} ({statusDetail}) und {unavailableResources.Count - 1} weitere Ressource(n) sind nicht im Verfügungsraum.";
    }

    private static string ResolveDisplayName(TeamResourceAvailabilityIssue issue, Team? team)
    {
        if (team is not null)
        {
            if (issue.EntityType == ResourceEntityType.Dog && !string.IsNullOrWhiteSpace(team.DogName))
                return team.DogName;

            if (issue.EntityType == ResourceEntityType.Person)
            {
                if (string.Equals(team.HundefuehrerId, issue.EntityId, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(team.HundefuehrerName))
                {
                    return team.HundefuehrerName;
                }

                var helperIndex = (team.HelferIds ?? new List<string>())
                    .FindIndex(id => string.Equals(id, issue.EntityId, StringComparison.Ordinal));
                if (helperIndex >= 0
                    && team.HelferNames is not null
                    && helperIndex < team.HelferNames.Count
                    && !string.IsNullOrWhiteSpace(team.HelferNames[helperIndex]))
                {
                    return team.HelferNames[helperIndex];
                }
            }
        }

        return $"Ressource '{issue.EntityId}'";
    }

    private static string FormatStatusLabel(ResourceStatus status) => status switch
    {
        ResourceStatus.Verfuegbar => "Im Verfügungsraum",
        ResourceStatus.VoruebergehendAbwesend => "Vorübergehend abwesend",
        ResourceStatus.Pause => "Pause",
        ResourceStatus.AufAnfahrt => "Auf Anfahrt",
        ResourceStatus.InSuche => "In Suche",
        ResourceStatus.Drohneneinsatz => "Drohneneinsatz",
        ResourceStatus.Sonderauftrag => "Sonderauftrag",
        ResourceStatus.Abgemeldet => "Abgemeldet",
        _ => status.ToString()
    };
}

using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Components.Monitor;

public sealed record TeamStartResourceResolution(
    ResourceEntityType EntityType,
    string EntityId,
    ResourceStatus? NewStatus,
    string? Note,
    bool RemoveFromTeam);

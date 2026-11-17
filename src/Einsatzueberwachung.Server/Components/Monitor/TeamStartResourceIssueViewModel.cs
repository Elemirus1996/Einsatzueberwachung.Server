using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Components.Monitor;

public sealed record TeamStartResourceIssueViewModel(
    ResourceEntityType EntityType,
    string EntityId,
    string Name,
    string Role,
    ResourceStatus? CurrentStatus,
    bool CanRemove);

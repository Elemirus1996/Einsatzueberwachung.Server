using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Models;

public sealed record TeamResourceAvailabilityIssue(
    ResourceEntityType EntityType,
    string EntityId,
    ResourceStatus? Status);

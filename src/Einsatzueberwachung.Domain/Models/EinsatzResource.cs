using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Models;

public sealed class EinsatzResource
{
    public ResourceEntityType EntityType { get; set; }
    public string EntityId { get; set; } = string.Empty;
    public ResourceStatus Status { get; set; }
    public string SonderauftragNotiz { get; set; } = string.Empty;
    public string DiveraRueckmeldung { get; set; } = string.Empty;
    public DateTime CheckedInAt { get; set; }
    public DateTime StatusChangedAt { get; set; }
}

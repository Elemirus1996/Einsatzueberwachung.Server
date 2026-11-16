namespace Einsatzueberwachung.Server.Components.Monitor;

public sealed record MonitorPoliceContactSummary(
    string PersonName,
    string? ContactName,
    string? ServiceNumber,
    string? Phone);

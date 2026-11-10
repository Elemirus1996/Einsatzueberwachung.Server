using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Server.Components.Monitor;

public sealed record MonitorSearchAreaItem(
    SearchArea Area,
    Team? AssignedTeam,
    string? TeamColor);

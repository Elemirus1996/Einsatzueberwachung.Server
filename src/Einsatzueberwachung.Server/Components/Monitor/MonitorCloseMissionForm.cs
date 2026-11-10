using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Components.Monitor;

public sealed class MonitorCloseMissionForm
{
    public bool ChecklistConfirmed { get; set; }
    public bool IncludeTracks { get; set; }
    public bool IncludePoints { get; set; }
    public MapTileType MapTileType { get; set; } = MapTileType.Streets;
    public string Result { get; set; } = string.Empty;
    public TimeOnly? EndTime { get; set; }
    public string Remarks { get; set; } = string.Empty;
}

public sealed record MonitorSelectionChange(string Id, bool IsSelected);

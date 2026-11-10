namespace Einsatzueberwachung.Server.Components.Monitor;

public class MonitorTeamEditorModel
{
    public string TeamType { get; set; } = "dog";
    public string TeamName { get; set; } = string.Empty;
    public string DogId { get; set; } = string.Empty;
    public string DroneId { get; set; } = string.Empty;
    public string HundefuehrerId { get; set; } = string.Empty;
    public string HelferId { get; set; } = string.Empty;
    public string HelferId2 { get; set; } = string.Empty;
    public string HelferId3 { get; set; } = string.Empty;
    public string SearchAreaId { get; set; } = string.Empty;
    public string CollarId { get; set; } = string.Empty;
    public int FirstWarningMinutes { get; set; } = 45;
    public int SecondWarningMinutes { get; set; } = 60;
    public string Notes { get; set; } = string.Empty;
}

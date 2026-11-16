namespace Einsatzueberwachung.Server.Components.Monitor;

public sealed record MonitorWeatherMetricTrend(
    bool IsAvailable,
    string IconClass,
    string Description,
    string CssClass)
{
    public static MonitorWeatherMetricTrend Unavailable { get; } = new(false, string.Empty, string.Empty, string.Empty);
}

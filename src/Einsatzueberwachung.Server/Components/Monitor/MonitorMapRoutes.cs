namespace Einsatzueberwachung.Server.Components.Monitor;

public static class MonitorMapRoutes
{
    public static string SearchAreaFocusUrl(string searchAreaId) =>
        $"/einsatz-karte?focusAreaId={Uri.EscapeDataString(searchAreaId)}";
}

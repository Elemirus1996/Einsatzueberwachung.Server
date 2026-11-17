using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Components.Shared;

public static class ResourceStatusPresentation
{
    public static string Label(ResourceStatus status) => status switch
    {
        ResourceStatus.Verfuegbar => "Im Verfügungsraum",
        ResourceStatus.VoruebergehendAbwesend => "Vorübergehend abwesend",
        ResourceStatus.Pause => "Pause",
        ResourceStatus.AufAnfahrt => "Auf Anfahrt",
        ResourceStatus.InSuche => "In Suche",
        ResourceStatus.Drohneneinsatz => "Drohneneinsatz",
        ResourceStatus.Sonderauftrag => "Sonderauftrag",
        ResourceStatus.Abgemeldet => "Abgemeldet",
        _ => status.ToString()
    };

    public static string Icon(ResourceStatus status) => status switch
    {
        ResourceStatus.Verfuegbar => "bi-check-circle",
        ResourceStatus.VoruebergehendAbwesend => "bi-clock-history",
        ResourceStatus.Pause => "bi-cup-hot",
        ResourceStatus.AufAnfahrt => "bi-truck",
        ResourceStatus.InSuche => "bi-geo-alt",
        ResourceStatus.Drohneneinsatz => "bi-drone",
        ResourceStatus.Sonderauftrag => "bi-clipboard-check",
        ResourceStatus.Abgemeldet => "bi-person-x",
        _ => "bi-question-circle"
    };

    public static string CssClass(ResourceStatus status) => status switch
    {
        ResourceStatus.Verfuegbar => "resource-status--available",
        ResourceStatus.VoruebergehendAbwesend or ResourceStatus.Pause or ResourceStatus.AufAnfahrt => "resource-status--pending",
        ResourceStatus.InSuche or ResourceStatus.Drohneneinsatz or ResourceStatus.Sonderauftrag => "resource-status--bound",
        ResourceStatus.Abgemeldet => "resource-status--off",
        _ => string.Empty
    };

    public static int Priority(ResourceStatus status) => status switch
    {
        ResourceStatus.Verfuegbar => 0,
        ResourceStatus.VoruebergehendAbwesend or ResourceStatus.Pause => 1,
        ResourceStatus.AufAnfahrt => 2,
        ResourceStatus.InSuche or ResourceStatus.Drohneneinsatz or ResourceStatus.Sonderauftrag => 3,
        ResourceStatus.Abgemeldet => 4,
        _ => 5
    };
}

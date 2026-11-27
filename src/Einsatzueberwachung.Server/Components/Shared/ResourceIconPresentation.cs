using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Components.Shared;

public static class ResourceIconPresentation
{
    public static string CssClass(ResourceEntityType entityType, string? collarMarkerIcon)
    {
        if (entityType == ResourceEntityType.Person)
            return "bi bi-person";

        if (entityType != ResourceEntityType.Dog)
            return "bi bi-question-circle";

        return collarMarkerIcon switch
        {
            "dog" => "fa-solid fa-dog",
            "bone" => "fa-solid fa-bone",
            "dot" => "fa-solid fa-location-dot",
            "crosshairs" => "fa-solid fa-crosshairs",
            "location-arrow" => "fa-solid fa-location-arrow",
            _ => "fa-solid fa-paw"
        };
    }
}

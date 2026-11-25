namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Esri World Imagery nur mit eigenem API-Schlüssel der Staffel (Issue 210). Ohne Schlüssel ist die Nutzung
/// der Esri-Dienste nicht geregelt; deshalb gibt es die Ebene dann gar nicht.
/// </summary>
public static class EsriKacheln
{
    /// <summary>Kachel-Adresse mit Platzhalter {token} (Esri: ibasemaps-api, Abrechnung über das Konto der Staffel)</summary>
    public const string Vorlage = "https://ibasemaps-api.arcgis.com/arcgis/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}?token={token}";

    /// <summary>Quellenvermerk, den Esri für Basiskarten verlangt</summary>
    public const string Quelle = "Powered by Esri | Esri, Maxar, Earthstar Geographics, and the GIS User Community";

    /// <summary>
    /// Adresse zum Prüfen des Schlüssels. Der Kacheldienst liefert auch bei falschem Schlüssel Bilder aus,
    /// der Basemap-Styles-Dienst lehnt ungültige Schlüssel dagegen mit 401/498 ab.
    /// </summary>
    public static string PruefAdresse(string schluessel) =>
        "https://basemapstyles-api.arcgis.com/arcgis/rest/services/styles/v2/styles/arcgis/imagery?token=" + Uri.EscapeDataString(schluessel.Trim());

    public static bool Vorhanden(string? schluessel) => !string.IsNullOrWhiteSpace(schluessel);

    public static string Adresse(string schluessel) => Vorlage.Replace("{token}", Uri.EscapeDataString(schluessel.Trim()));
}

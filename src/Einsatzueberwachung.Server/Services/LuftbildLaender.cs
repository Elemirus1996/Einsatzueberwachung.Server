using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Ein Luftbild-Dienst (WMS, DOP20) einer Landesvermessung.</summary>
public sealed record LuftbildQuelle(string Land, string Name, string Url, string Ebenen, string Version, double[] Box, bool Cors, string Quelle)
{
    public bool Schneidet(double west, double sued, double ost, double nord) =>
        Box[0] < ost && Box[2] > west && Box[1] < nord && Box[3] > sued;

    /// <summary>GetMap-Adresse für einen Ausschnitt in Web-Mercator (EPSG:3857), PNG mit durchsichtigem Rand.</summary>
    public string KachelUrl(double minX, double minY, double maxX, double maxY, int pixel)
    {
        var crs = Version == "1.1.1" ? "SRS" : "CRS";
        var box = string.Join(',', new[] { minX, minY, maxX, maxY }.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));
        return $"{Url}{(Url.Contains('?') ? '&' : '?')}SERVICE=WMS&REQUEST=GetMap&VERSION={Version}" +
               $"&LAYERS={Uri.EscapeDataString(Ebenen)}&STYLES=&FORMAT=image%2Fpng&TRANSPARENT=TRUE" +
               $"&{crs}=EPSG%3A3857&BBOX={box}&WIDTH={pixel}&HEIGHT={pixel}";
    }

    public string Quellenvermerk(int jahr) => Quelle.Replace("{jahr}", jahr.ToString(CultureInfo.InvariantCulture));
}

/// <summary>
/// Amtliche Luftbilder der Länder (Issue 210). Alle Dienste zusammen ergeben eine Ebene „Luftbild (Länder)“,
/// damit auch über Landesgrenzen hinweg gearbeitet werden kann. Die Liste liegt in wwwroot/data/luftbild-laender.json
/// und wird von der Karte im Browser und vom PDF-Bericht gemeinsam genutzt.
/// </summary>
public static class LuftbildLaender
{
    private static readonly Lazy<IReadOnlyList<LuftbildQuelle>> _alle = new(Laden);

    public static IReadOnlyList<LuftbildQuelle> Alle => _alle.Value;

    public static IReadOnlyList<LuftbildQuelle> Fuer(double west, double sued, double ost, double nord) =>
        Alle.Where(q => q.Schneidet(west, sued, ost, nord)).ToList();

    public static string Quellenvermerk(IEnumerable<LuftbildQuelle> quellen, int jahr)
    {
        var teile = quellen.Select(q => q.Quellenvermerk(jahr)).ToList();
        return teile.Count == 0 ? "Luftbild: keine Daten der Länder" : "Luftbild: " + string.Join(" | ", teile);
    }

    private static IReadOnlyList<LuftbildQuelle> Laden()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("luftbild-laender.json")
            ?? throw new InvalidOperationException("luftbild-laender.json fehlt als eingebettete Ressource");
        return JsonSerializer.Deserialize<List<LuftbildQuelle>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}

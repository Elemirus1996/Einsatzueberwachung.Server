using Einsatzueberwachung.Server.Components.Pages;

namespace Einsatzueberwachung.Server.Security;

/// <summary>
/// Oeffentlicher Team-Zugang (Audit 2026-10-24, KRITISCH): Der oeffentliche Nginx-Block leitet auf einen
/// eigenen Kestrel-Port. Alles, was ueber diesen Port kommt, darf nur die Team-Mobile-Ansicht erreichen –
/// auch innerhalb eines Blazor-Circuits, in dem Seitenwechsel ohne HTTP-Request ablaufen und Nginx sie nicht sieht.
/// </summary>
public sealed class PublicAccess
{
    /// <summary>Umgebungsvariable mit dem Port des oeffentlichen Zugangs (z.B. 5002). Leer = kein oeffentlicher Zugang.</summary>
    public const string PortVariable = "EINSATZUEBERWACHUNG_PUBLIC_PORT";

    // Seiten, die ueber den oeffentlichen Zugang gerendert werden duerfen
    private static readonly HashSet<Type> PublicPages = [typeof(TeamLogin), typeof(TeamMobile)];

    // Pfade fuer die Team-Ansicht: Seiten, Team-API, Team-Hub, Blazor-Circuit und Framework-Dateien
    private static readonly string[] PublicPathPrefixes =
    [
        "/team", "/api/team-mobile", "/hubs/team-mobile",
        "/_blazor", "/_framework", "/_content",
        "/css", "/js", "/lib",
    ];

    // Statische Dateien direkt unter wwwroot (app.css, leaflet-interop.js, favicon.png, ...)
    private static readonly HashSet<string> PublicRootFileExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".css", ".js", ".png", ".ico", ".svg", ".woff", ".woff2", ".map" };

    public PublicAccess(int port) => Port = port;

    /// <summary>Port des oeffentlichen Zugangs, 0 = nicht eingerichtet.</summary>
    public int Port { get; }

    public static PublicAccess FromConfiguration(IConfiguration configuration) =>
        new(int.TryParse(configuration[PortVariable], out var port) && port is > 0 and <= 65535 ? port : 0);

    /// <summary>Kam die Anfrage ueber den oeffentlichen Port? Massgeblich ist der lokale Port, den Header nicht faelschen koennen.</summary>
    public bool IsPublicRequest(HttpContext? http) =>
        Port > 0 && http is not null && http.Connection.LocalPort == Port;

    public static bool IsPublicPath(PathString path)
    {
        var value = path.Value ?? "";
        if (value.Contains("..", StringComparison.Ordinal)) return false;

        foreach (var prefix in PublicPathPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        // Nur Dateien direkt im Wurzelverzeichnis, keine Unterordner wie /downloads/...
        return value.LastIndexOf('/') == 0 && PublicRootFileExtensions.Contains(Path.GetExtension(value));
    }

    public static bool IsPublicPage(Type? pageType) => pageType is not null && PublicPages.Contains(pageType);
}

/// <summary>Weist auf dem oeffentlichen Port alles ab, was nicht zur Team-Ansicht gehoert.</summary>
public sealed class PublicAccessMiddleware(RequestDelegate next, PublicAccess publicAccess)
{
    public Task InvokeAsync(HttpContext http)
    {
        if (publicAccess.IsPublicRequest(http) && !PublicAccess.IsPublicPath(http.Request.Path))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        return next(http);
    }
}

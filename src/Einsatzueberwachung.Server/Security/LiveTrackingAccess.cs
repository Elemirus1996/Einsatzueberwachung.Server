using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Einsatzueberwachung.Domain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Einsatzueberwachung.Server.Security;

/// <summary>
/// API-Schluessel der LiveTracking-App (1.10, Audit 2026-10-24: der Halsband-Webhook nahm von jedem Positionen an).
/// Liegt in einer eigenen Datei im Datenverzeichnis, nicht im Backup.
/// </summary>
public sealed class LiveTrackingAccess
{
    public const string FileName = "livetracking-key.txt";
    public const string HeaderName = "X-Api-Key";

    /// <summary>Kennung am Anfang des Verbindungscodes, damit die App ihn von einer Adresse unterscheidet.</summary>
    public const string CodePrefix = "EUL1.";

    private readonly string _filePath;
    private readonly object _lock = new();
    private string _apiKey;

    public LiveTrackingAccess(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, FileName);
        _apiKey = File.Exists(_filePath) ? File.ReadAllText(_filePath).Trim() : string.Empty;
        if (_apiKey.Length < 32)
        {
            _apiKey = NewKey();
            File.WriteAllText(_filePath, _apiKey);
        }
    }

    public static LiveTrackingAccess ForDataDirectory() => new(AppPathResolver.GetDataDirectory());

    public string ApiKey { get { lock (_lock) return _apiKey; } }

    /// <summary>Neuer Schluessel – die App braucht danach den neuen Verbindungscode (z.B. wenn ein Laptop verloren ging).</summary>
    public void Regenerate()
    {
        lock (_lock)
        {
            _apiKey = NewKey();
            File.WriteAllText(_filePath, _apiKey);
        }
    }

    public bool IsValid(string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        var current = ApiKey;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(current));
    }

    /// <summary>
    /// Verbindungscode fuer die App: Server-Adresse, API-Schluessel und SHA-256-Fingerabdruck des Server-Zertifikats.
    /// Die App vertraut einem Zertifikat, das Windows ablehnt (selbst ausgestellt), nur wenn der Fingerabdruck passt.
    /// </summary>
    public string BuildConnectionCode(string serverUrl, string? certificateSha256)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new ConnectionInfo(serverUrl.TrimEnd('/'), ApiKey, certificateSha256));
        return CodePrefix + Convert.ToBase64String(json).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static ConnectionInfo? ParseConnectionCode(string code)
    {
        if (!code.StartsWith(CodePrefix, StringComparison.Ordinal)) return null;
        var base64 = code[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return JsonSerializer.Deserialize<ConnectionInfo>(Convert.FromBase64String(base64));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string NewKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Kurze Schluessel fuer einen kompakten Code: u = Adresse, k = API-Schluessel, f = Fingerabdruck.</summary>
    public sealed record ConnectionInfo(
        [property: System.Text.Json.Serialization.JsonPropertyName("u")] string ServerUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("k")] string ApiKey,
        [property: System.Text.Json.Serialization.JsonPropertyName("f")] string? CertificateSha256);
}

/// <summary>Nur Anfragen mit gueltigem API-Schluessel der LiveTracking-App (Header X-Api-Key).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireLiveTrackingKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var access = context.HttpContext.RequestServices.GetRequiredService<LiveTrackingAccess>();
        if (!access.IsValid(context.HttpContext.Request.Headers[LiveTrackingAccess.HeaderName]))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "API-Schlüssel der LiveTracking-App fehlt oder ist ungültig." });
        }
    }
}

/// <summary>Ermittelt den SHA-256-Fingerabdruck des Zertifikats, das Browser und App unter einer Adresse sehen.</summary>
public static class ServerCertificateProbe
{
    public static async Task<string?> GetSha256Async(Uri address, CancellationToken cancellationToken = default)
    {
        if (address.Scheme != Uri.UriSchemeHttps) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(address.Host, address.Port, timeout.Token);
            // Nur auslesen, nicht vertrauen – genau dafuer wird der Fingerabdruck ja ermittelt
            await using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = address.Host }, timeout.Token);
            return ssl.RemoteCertificate is { } certificate
                ? Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()))
                : null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
            return null;
        }
    }
}

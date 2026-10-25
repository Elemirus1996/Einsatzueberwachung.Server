using System.Text.Json;
using System.Text.Json.Nodes;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Server.Security;

/// <summary>
/// Zugangsdaten (GitHub-Token, Divera-Accesskey) verlassen den Server nicht ueber Downloads (Audit 2026-10-24).
/// Beim Einspielen einer Datei ohne Zugangsdaten bleiben die vorhandenen erhalten.
/// </summary>
public static class SettingsSecrets
{
    private static readonly string[] SecretProperties = [nameof(AppSettings.GitHubToken), nameof(AppSettings.DiveraAccessKey)];

    // Dateien im Datenverzeichnis, die nie ins Daten-Backup kommen (HMAC-Schluessel der Team-Cookies,
    // Passwort-Hash und Einrichtungscode der Einsatzleitung – auf einem neuen Server wird neu eingerichtet)
    private static readonly HashSet<string> BackupExcludedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "team-mobile-secret.bin", LeitungAuthStore.FileName, LeitungAuthStore.SetupCodeFileName, LiveTrackingAccess.FileName,
        Services.VpnDeviceStore.FileName,
    };

    /// <summary>AppSettings als JSON ohne Zugangsdaten.</summary>
    public static byte[] SerializeWithoutSecrets(AppSettings settings) =>
        MaskJson(JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Zugangsdaten in einer AppSettings-JSON-Datei leeren, alles andere unveraendert lassen.</summary>
    public static byte[] MaskJson(byte[] json)
    {
        if (JsonNode.Parse(json) is not JsonObject root) return json;
        foreach (var property in root.Select(p => p.Key).ToList())
        {
            if (SecretProperties.Contains(property, StringComparer.OrdinalIgnoreCase))
                root[property] = string.Empty;
        }
        return JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Leere Zugangsdaten in importierten Einstellungen durch die aktuell gespeicherten ersetzen.</summary>
    public static void KeepExistingSecrets(AppSettings imported, AppSettings current)
    {
        if (string.IsNullOrWhiteSpace(imported.GitHubToken)) imported.GitHubToken = current.GitHubToken;
        if (string.IsNullOrWhiteSpace(imported.DiveraAccessKey)) imported.DiveraAccessKey = current.DiveraAccessKey;
    }

    public static bool IsExcludedFromBackup(string relativePath) =>
        BackupExcludedFiles.Contains(Path.GetFileName(relativePath));

    public static bool IsAppSettingsFile(string relativePath) =>
        string.Equals(relativePath.Replace('\\', '/'), "AppSettings.json", StringComparison.OrdinalIgnoreCase);
}

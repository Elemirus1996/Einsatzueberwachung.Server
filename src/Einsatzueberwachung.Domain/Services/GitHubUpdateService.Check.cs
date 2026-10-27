using System.Net.Http.Headers;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class GitHubUpdateService
    {
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            SetStatus(status =>
            {
                status.IsChecking = true;
                status.LastMessage = "Pruefe auf neue Version...";
            });

            try
            {
                var url = await ResolveReleaseApiUrlAsync();
                var githubToken = await ResolveGitHubTokenAsync();
                _logger.LogInformation("Prüfe GitHub auf Updates: {Url}", url);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

                if (!string.IsNullOrWhiteSpace(githubToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GitHub API Fehler: {StatusCode}", response.StatusCode);
                    var responseBody = await response.Content.ReadAsStringAsync();
                    var message = response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "GitHub API Fehler: NotFound (Repository privat? Bitte GitHub Token konfigurieren)"
                        : $"GitHub API Fehler: {response.StatusCode}";

                    if (!string.IsNullOrWhiteSpace(responseBody))
                        _logger.LogDebug("GitHub API Response: {Body}", responseBody);

                    SetStatus(status =>
                    {
                        status.LastCheckedAt = Ortszeit.Jetzt;
                        status.LastError = message;
                        status.LastMessage = "Update-Pruefung fehlgeschlagen";
                    });

                    return new UpdateCheckResult { Success = false, ErrorMessage = message, CheckedAt = Ortszeit.Jetzt };
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var tagName = root.GetProperty("tag_name").GetString() ?? "unknown";
                var version = NormalizeVersion(tagName);
                var downloadUrl = root.GetProperty("html_url").GetString() ?? string.Empty;
                var releaseNotes = root.GetProperty("body").GetString() ?? string.Empty;

                var assets = root.GetProperty("assets");
                string? installerUrl = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (IsLinuxX64Asset(name))
                    {
                        installerUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                var newerVersionAvailable = !string.IsNullOrWhiteSpace(version) && IsNewerVersion(version, CurrentVersion);
                var result = new UpdateCheckResult
                {
                    Success = true,
                    CurrentVersion = CurrentVersion,
                    LatestVersion = version,
                    ReleaseUrl = downloadUrl,
                    InstallerUrl = installerUrl,
                    ReleaseNotes = releaseNotes,
                    CheckedAt = Ortszeit.Jetzt,
                    UpdateAvailable = newerVersionAvailable,
                    IsInstallable = !string.IsNullOrWhiteSpace(installerUrl)
                };

                LastCheckResult = result;
                SetStatus(status =>
                {
                    status.CurrentVersion = CurrentVersion;
                    status.LatestVersion = result.LatestVersion;
                    status.LastCheckedAt = result.CheckedAt;
                    status.UpdateAvailable = result.UpdateAvailable;
                    status.LastError = null;
                    status.LastMessage = result.UpdateAvailable
                        ? (result.IsInstallable
                            ? $"Update verfuegbar: {CurrentVersion} -> {result.LatestVersion}"
                            : $"Neue Version erkannt ({result.LatestVersion}), aber kein Linux-Installer im Release")
                        : "System ist aktuell";
                });

                if (result.UpdateAvailable)
                {
                    if (result.IsInstallable)
                        _logger.LogInformation("Update verfügbar: {Current} -> {Latest}", CurrentVersion, version);
                    else
                        _logger.LogWarning("Neue Version {Latest} erkannt, aber kein Linux-x64-Asset vorhanden", version);
                }
                else
                {
                    _logger.LogInformation("Anwendung ist aktuell");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Prüfen auf Updates");
                SetStatus(status =>
                {
                    status.LastCheckedAt = Ortszeit.Jetzt;
                    status.LastError = ex.Message;
                    status.LastMessage = "Update-Pruefung fehlgeschlagen";
                });
                return new UpdateCheckResult { Success = false, ErrorMessage = ex.Message, CheckedAt = Ortszeit.Jetzt };
            }
            finally
            {
                SetStatus(status => status.IsChecking = false);
            }
        }

        public async Task<byte[]?> DownloadInstallerAsync(string url)
        {
            try
            {
                _logger.LogInformation("Downloade Installer: {Url}", url);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                var githubToken = await ResolveGitHubTokenAsync();
                if (!string.IsNullOrWhiteSpace(githubToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Installer-Download fehlgeschlagen: {StatusCode} - {Body}", response.StatusCode, body);
                    return null;
                }

                var data = await response.Content.ReadAsByteArrayAsync();
                _logger.LogInformation("Installer erfolgreich heruntergeladen: {Size} bytes", data.Length);
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Download des Installers");
                return null;
            }
        }

        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            try
            {
                return ParseVersionOrDefault(latestVersion) > ParseVersionOrDefault(currentVersion);
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> ResolveReleaseApiUrlAsync()
        {
            var defaultUrl = string.Format(GITHUB_API_URL, GITHUB_OWNER, GITHUB_REPO);
            var appSettings = await _settingsService.GetAppSettingsAsync();
            var configured = appSettings.UpdateUrl?.Trim();
            if (string.IsNullOrWhiteSpace(configured))
                return defaultUrl;

            if (configured.StartsWith("https://api.github.com/", StringComparison.OrdinalIgnoreCase))
                return configured;

            if (!Uri.TryCreate(configured, UriKind.Absolute, out var parsedUri))
                return defaultUrl;

            if (parsedUri.Host.ToLowerInvariant() is not "github.com")
                return defaultUrl;

            var segments = parsedUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3)
                return defaultUrl;

            var owner = segments[0];
            var repo = segments[1];
            if (!segments[2].Equals("releases", StringComparison.OrdinalIgnoreCase))
                return defaultUrl;

            if (segments.Length == 3 || segments[3].Equals("latest", StringComparison.OrdinalIgnoreCase))
                return $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            if (segments[3].Equals("tag", StringComparison.OrdinalIgnoreCase) && segments.Length >= 5)
                return $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(segments[4])}";

            return defaultUrl;
        }

        private async Task<string?> ResolveGitHubTokenAsync()
        {
            var token = Environment.GetEnvironmentVariable("EINSATZUEBERWACHUNG_GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
                return token.Trim();

            token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
                return token.Trim();

            var appSettings = await _settingsService.GetAppSettingsAsync();
            return string.IsNullOrWhiteSpace(appSettings.GitHubToken) ? null : appSettings.GitHubToken.Trim();
        }
    }
}

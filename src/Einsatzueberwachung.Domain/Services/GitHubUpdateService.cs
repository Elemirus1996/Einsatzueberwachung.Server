using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Service für die Verwaltung von GitHub-Release Updates
    /// Prüft GitHub auf neue Releases und verwaltet Update-Prozesse
    /// </summary>
    public class GitHubUpdateService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GitHubUpdateService> _logger;
        private readonly SemaphoreSlim _updateGate = new(1, 1);
        private readonly object _statusLock = new();
        
        // GitHub API Konfiguration
        private const string GITHUB_OWNER = "Elemirus1996";
        private const string GITHUB_REPO = "Einsatzueberwachung.Server";
        private const string GITHUB_API_URL = "https://api.github.com/repos/{0}/{1}/releases/latest";
        
        public string CurrentVersion { get; set; } = ResolveCurrentVersion();
        public UpdateCheckResult? LastCheckResult { get; set; }
        public UpdateRuntimeStatus RuntimeStatus { get; }

        public GitHubUpdateService(HttpClient httpClient, ILogger<GitHubUpdateService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            RuntimeStatus = new UpdateRuntimeStatus
            {
                CurrentVersion = CurrentVersion,
                LastMessage = "Updater bereit"
            };
            
            // User-Agent für GitHub API erforderlich
            if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Einsatzueberwachung-Update-Checker");
            }
        }

        public UpdateRuntimeStatus GetStatusSnapshot()
        {
            lock (_statusLock)
            {
                return RuntimeStatus.Clone();
            }
        }

        /// <summary>
        /// Prüft GitHub auf neue Releases
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            SetStatus(status =>
            {
                status.IsChecking = true;
                status.LastMessage = "Pruefe auf neue Version...";
            });

            try
            {
                var url = string.Format(GITHUB_API_URL, GITHUB_OWNER, GITHUB_REPO);
                _logger.LogInformation("Prüfe GitHub auf Updates: {Url}", url);

                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GitHub API Fehler: {StatusCode}", response.StatusCode);
                    return new UpdateCheckResult
                    {
                        Success = false,
                        ErrorMessage = $"GitHub API Fehler: {response.StatusCode}",
                        CheckedAt = DateTime.Now
                    };
                }

                var json = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                var tagName = root.GetProperty("tag_name").GetString() ?? "unknown";
                var version = tagName.TrimStart('v');
                var downloadUrl = root.GetProperty("html_url").GetString() ?? string.Empty;
                var releaseNotes = root.GetProperty("body").GetString() ?? string.Empty;

                // Assets (Dateien) aus dem Release holen
                var assets = root.GetProperty("assets");
                string? installerUrl = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("installer", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("linux", StringComparison.OrdinalIgnoreCase))
                    {
                        installerUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                var result = new UpdateCheckResult
                {
                    Success = true,
                    CurrentVersion = CurrentVersion,
                    LatestVersion = version,
                    ReleaseUrl = downloadUrl,
                    InstallerUrl = installerUrl,
                    ReleaseNotes = releaseNotes,
                    CheckedAt = DateTime.Now,
                    UpdateAvailable = IsNewerVersion(version, CurrentVersion)
                };

                LastCheckResult = result;
                SetStatus(status =>
                {
                    status.CurrentVersion = CurrentVersion;
                    status.LatestVersion = result.LatestVersion;
                    status.LastCheckedAt = result.CheckedAt;
                    status.UpdateAvailable = result.UpdateAvailable;
                    status.LastMessage = result.UpdateAvailable
                        ? $"Update verfuegbar: {CurrentVersion} -> {result.LatestVersion}"
                        : "System ist aktuell";
                });
                
                if (result.UpdateAvailable)
                {
                    _logger.LogInformation("Update verfügbar: {Current} → {Latest}", 
                        CurrentVersion, version);
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
                    status.LastCheckedAt = DateTime.Now;
                    status.LastError = ex.Message;
                    status.LastMessage = "Update-Pruefung fehlgeschlagen";
                });
                return new UpdateCheckResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    CheckedAt = DateTime.Now
                };
            }
            finally
            {
                SetStatus(status => status.IsChecking = false);
            }
        }

        public async Task<UpdateInstallResult> InstallLatestAsync(CancellationToken cancellationToken = default)
        {
            if (!await _updateGate.WaitAsync(0, cancellationToken))
            {
                return new UpdateInstallResult
                {
                    Success = false,
                    Message = "Es laeuft bereits ein Update-Prozess."
                };
            }

            try
            {
                SetStatus(status =>
                {
                    status.IsInstalling = true;
                    status.LastError = null;
                    status.LastMessage = "Update wird vorbereitet...";
                });

                var check = await CheckForUpdatesAsync();
                if (!check.Success)
                {
                    return new UpdateInstallResult
                    {
                        Success = false,
                        Message = check.ErrorMessage ?? "Update-Pruefung fehlgeschlagen."
                    };
                }

                if (!check.UpdateAvailable)
                {
                    return new UpdateInstallResult
                    {
                        Success = true,
                        Message = "Es ist bereits die neueste Version installiert.",
                        InstalledVersion = CurrentVersion
                    };
                }

                if (string.IsNullOrWhiteSpace(check.InstallerUrl))
                {
                    return new UpdateInstallResult
                    {
                        Success = false,
                        Message = "Kein passendes Linux-Release-Asset gefunden."
                    };
                }

                var updatesRoot = Path.Combine(AppPathResolver.GetDataDirectory(), "updates");
                var downloadsPath = Path.Combine(updatesRoot, "downloads");
                var releasesPath = Path.Combine(updatesRoot, "releases");
                Directory.CreateDirectory(downloadsPath);
                Directory.CreateDirectory(releasesPath);

                SetStatus(status => status.LastMessage = "Lade Update-Paket herunter...");
                var packageBytes = await DownloadInstallerAsync(check.InstallerUrl);
                if (packageBytes is null || packageBytes.Length == 0)
                {
                    return new UpdateInstallResult
                    {
                        Success = false,
                        Message = "Download des Update-Pakets fehlgeschlagen."
                    };
                }

                var extension = ResolveExtensionFromUrl(check.InstallerUrl);
                var packagePath = Path.Combine(downloadsPath, $"{check.LatestVersion}{extension}");
                await File.WriteAllBytesAsync(packagePath, packageBytes, cancellationToken);

                var stagePath = Path.Combine(releasesPath, check.LatestVersion);
                if (Directory.Exists(stagePath))
                {
                    Directory.Delete(stagePath, recursive: true);
                }
                Directory.CreateDirectory(stagePath);

                SetStatus(status => status.LastMessage = "Entpacke Update-Paket...");
                await ExtractPackageAsync(packagePath, stagePath, cancellationToken);

                SetStatus(status => status.LastMessage = "Wende Update an...");
                var applyResult = await ApplyUpdateAsync(stagePath, packagePath, check.LatestVersion, cancellationToken);

                if (!applyResult.Success)
                {
                    SetStatus(status =>
                    {
                        status.LastError = applyResult.Message;
                        status.LastMessage = "Update fehlgeschlagen";
                    });
                    return applyResult;
                }

                SetStatus(status =>
                {
                    status.CurrentVersion = check.LatestVersion;
                    status.LatestVersion = check.LatestVersion;
                    status.UpdateAvailable = false;
                    status.LastInstalledAt = DateTime.Now;
                    status.LastMessage = applyResult.Message;
                });

                return applyResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler bei der Update-Installation");
                SetStatus(status =>
                {
                    status.LastError = ex.Message;
                    status.LastMessage = "Update fehlgeschlagen";
                });

                return new UpdateInstallResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
            finally
            {
                SetStatus(status => status.IsInstalling = false);
                _updateGate.Release();
            }
        }

        /// <summary>
        /// Vergleicht zwei Versionsnummern
        /// </summary>
        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            try
            {
                var latest = Version.Parse(latestVersion);
                var current = Version.Parse(currentVersion);
                return latest > current;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Lädt die Installer-Datei herunter
        /// </summary>
        public async Task<byte[]?> DownloadInstallerAsync(string url)
        {
            try
            {
                _logger.LogInformation("Downloade Installer: {Url}", url);
                var data = await _httpClient.GetByteArrayAsync(url);
                _logger.LogInformation("Installer erfolgreich heruntergeladen: {Size} bytes", data.Length);
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Download des Installers");
                return null;
            }
        }

        private static string ResolveCurrentVersion()
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null ? "0.0.0" : version.ToString(3);
        }

        private static string ResolveExtensionFromUrl(string url)
        {
            if (url.Contains(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                return ".tar.gz";
            }

            if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return ".zip";
            }

            return ".bin";
        }

        private async Task ExtractPackageAsync(string packagePath, string stagePath, CancellationToken cancellationToken)
        {
            if (packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(packagePath, stagePath, overwriteFiles: true);
                return;
            }

            if (packagePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                var extract = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    ArgumentList =
                    {
                        "-lc",
                        $"tar -xzf '{packagePath.Replace("'", "'\\''")}' -C '{stagePath.Replace("'", "'\\''")}'"
                    },
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(extract);
                if (process is null)
                {
                    throw new InvalidOperationException("tar-Prozess konnte nicht gestartet werden.");
                }

                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                    throw new InvalidOperationException($"Entpacken fehlgeschlagen: {error}");
                }

                return;
            }

            throw new InvalidOperationException("Unbekanntes Paketformat. Erwartet .zip oder .tar.gz");
        }

        private async Task<UpdateInstallResult> ApplyUpdateAsync(
            string stagePath,
            string packagePath,
            string targetVersion,
            CancellationToken cancellationToken)
        {
            var applyCommand = Environment.GetEnvironmentVariable("EINSATZUEBERWACHUNG_UPDATE_APPLY_CMD");
            if (!string.IsNullOrWhiteSpace(applyCommand))
            {
                var cmd = applyCommand
                    .Replace("{stage}", stagePath, StringComparison.OrdinalIgnoreCase)
                    .Replace("{package}", packagePath, StringComparison.OrdinalIgnoreCase)
                    .Replace("{version}", targetVersion, StringComparison.OrdinalIgnoreCase);

                var info = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    ArgumentList = { "-lc", cmd },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(info);
                if (process is null)
                {
                    return new UpdateInstallResult
                    {
                        Success = false,
                        Message = "Update-Kommando konnte nicht gestartet werden."
                    };
                }

                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                    return new UpdateInstallResult
                    {
                        Success = false,
                        Message = $"Update-Kommando fehlgeschlagen: {error}"
                    };
                }

                CurrentVersion = targetVersion;
                return new UpdateInstallResult
                {
                    Success = true,
                    Message = "Update erfolgreich angewendet. Dienste werden neugestartet.",
                    InstalledVersion = targetVersion
                };
            }

            return new UpdateInstallResult
            {
                Success = true,
                InstalledVersion = targetVersion,
                Message = "Update bereitgestellt. Fuer automatisches Anwenden bitte EINSATZUEBERWACHUNG_UPDATE_APPLY_CMD konfigurieren."
            };
        }

        private void SetStatus(Action<UpdateRuntimeStatus> mutate)
        {
            lock (_statusLock)
            {
                mutate(RuntimeStatus);
            }
        }
    }

    /// <summary>
    /// Ergebnis einer Update-Prüfung
    /// </summary>
    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string? InstallerUrl { get; set; }
        public string ReleaseNotes { get; set; } = string.Empty;
        public DateTime CheckedAt { get; set; }
        public bool UpdateAvailable { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class UpdateInstallResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? InstalledVersion { get; set; }
    }

    public class UpdateRuntimeStatus
    {
        public string CurrentVersion { get; set; } = "0.0.0";
        public string LatestVersion { get; set; } = string.Empty;
        public bool UpdateAvailable { get; set; }
        public bool IsChecking { get; set; }
        public bool IsInstalling { get; set; }
        public DateTime? LastCheckedAt { get; set; }
        public DateTime? LastInstalledAt { get; set; }
        public string? LastError { get; set; }
        public string LastMessage { get; set; } = string.Empty;

        public UpdateRuntimeStatus Clone()
        {
            return (UpdateRuntimeStatus)MemberwiseClone();
        }
    }
}





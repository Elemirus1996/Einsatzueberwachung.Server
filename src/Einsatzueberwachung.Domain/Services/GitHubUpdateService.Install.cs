using System.Diagnostics;
using System.IO.Compression;
using Einsatzueberwachung.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class GitHubUpdateService
    {
        public async Task<UpdateInstallResult> InstallLatestAsync(CancellationToken cancellationToken = default)
        {
            if (!await _updateGate.WaitAsync(0, cancellationToken))
                return new UpdateInstallResult { Success = false, Message = "Es laeuft bereits ein Update-Prozess." };

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
                    var message = check.ErrorMessage ?? "Update-Pruefung fehlgeschlagen.";
                    SetStatus(status => { status.LastError = message; status.LastMessage = message; });
                    return new UpdateInstallResult { Success = false, Message = message };
                }

                if (!check.UpdateAvailable)
                {
                    SetStatus(status =>
                    {
                        status.UpdateAvailable = false;
                        status.LastError = null;
                        status.LastMessage = "Es ist bereits die neueste Version installiert.";
                    });
                    return new UpdateInstallResult { Success = true, Message = "Es ist bereits die neueste Version installiert.", InstalledVersion = CurrentVersion };
                }

                if (string.IsNullOrWhiteSpace(check.InstallerUrl))
                {
                    const string message = "Neue Version gefunden, aber kein passendes Linux-Release-Asset gefunden.";
                    SetStatus(status => { status.LastError = message; status.LastMessage = message; });
                    return new UpdateInstallResult { Success = false, Message = message };
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
                    const string message = "Download des Update-Pakets fehlgeschlagen.";
                    SetStatus(status => { status.LastError = message; status.LastMessage = message; });
                    return new UpdateInstallResult { Success = false, Message = message };
                }

                var extension = ResolveExtensionFromUrl(check.InstallerUrl);
                var packagePath = Path.Combine(downloadsPath, $"{check.LatestVersion}{extension}");
                await File.WriteAllBytesAsync(packagePath, packageBytes, cancellationToken);

                var stagePath = Path.Combine(releasesPath, check.LatestVersion);
                if (Directory.Exists(stagePath))
                    Directory.Delete(stagePath, recursive: true);
                Directory.CreateDirectory(stagePath);

                SetStatus(status => status.LastMessage = "Entpacke Update-Paket...");
                await ExtractPackageAsync(packagePath, stagePath, cancellationToken);

                SetStatus(status => status.LastMessage = "Wende Update an...");
                var applyResult = await ApplyUpdateAsync(stagePath, packagePath, check.LatestVersion, cancellationToken);

                if (!applyResult.Success)
                {
                    SetStatus(status => { status.LastError = applyResult.Message; status.LastMessage = "Update fehlgeschlagen"; });
                    return applyResult;
                }

                SetStatus(status =>
                {
                    status.CurrentVersion = check.LatestVersion;
                    status.LatestVersion = check.LatestVersion;
                    status.UpdateAvailable = false;
                    status.LastInstalledAt = Ortszeit.Jetzt;
                    status.LastMessage = applyResult.Message;
                });

                return applyResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler bei der Update-Installation");
                SetStatus(status => { status.LastError = ex.Message; status.LastMessage = "Update fehlgeschlagen"; });
                return new UpdateInstallResult { Success = false, Message = ex.Message };
            }
            finally
            {
                SetStatus(status => status.IsInstalling = false);
                _updateGate.Release();
            }
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
                    ArgumentList = { "-lc", $"tar -xzf '{packagePath.Replace("'", "'\\''")}' -C '{stagePath.Replace("'", "'\\''")}'" },
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(extract)
                    ?? throw new InvalidOperationException("tar-Prozess konnte nicht gestartet werden.");
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
                var hasPackagePlaceholder = applyCommand.Contains("{package}", StringComparison.OrdinalIgnoreCase);
                var hasVersionPlaceholder = applyCommand.Contains("{version}", StringComparison.OrdinalIgnoreCase);

                var cmd = applyCommand
                    .Replace("{stage}", stagePath, StringComparison.OrdinalIgnoreCase)
                    .Replace("{package}", packagePath, StringComparison.OrdinalIgnoreCase)
                    .Replace("{version}", targetVersion, StringComparison.OrdinalIgnoreCase);

                if (!hasPackagePlaceholder)
                    cmd = $"{cmd} {EscapeForBashSingleQuoted(packagePath)}";
                if (!hasVersionPlaceholder)
                    cmd = $"{cmd} {EscapeForBashSingleQuoted(targetVersion)}";

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
                    return new UpdateInstallResult { Success = false, Message = "Update-Kommando konnte nicht gestartet werden." };

                await process.WaitForExitAsync(cancellationToken);
                var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    var details = string.IsNullOrWhiteSpace(stderr) ? stdout : $"STDERR: {stderr}\nSTDOUT: {stdout}";
                    return new UpdateInstallResult
                    {
                        Success = false,
                        Message = $"Update-Kommando fehlgeschlagen (ExitCode {process.ExitCode}): {details}".Trim()
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
    }
}

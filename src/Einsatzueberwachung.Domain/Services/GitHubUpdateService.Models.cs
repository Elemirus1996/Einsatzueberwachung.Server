namespace Einsatzueberwachung.Domain.Services
{
    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string? InstallerUrl { get; set; }
        public bool IsInstallable { get; set; }
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
        /// <summary>Versionshinweise (Markdown) des neuesten Releases, zum Anzeigen unter Einstellungen → Updates</summary>
        public string LatestReleaseNotes { get; set; } = string.Empty;
        public string LatestReleaseUrl { get; set; } = string.Empty;

        public UpdateRuntimeStatus Clone() => (UpdateRuntimeStatus)MemberwiseClone();
    }
}

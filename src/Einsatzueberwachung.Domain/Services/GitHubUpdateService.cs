using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Einsatzueberwachung.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class GitHubUpdateService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GitHubUpdateService> _logger;
        private readonly SemaphoreSlim _updateGate = new(1, 1);
        private readonly object _statusLock = new();
        private readonly ISettingsService _settingsService;

        private const string GITHUB_OWNER = "Elemirus1996";
        private const string GITHUB_REPO = "Einsatzueberwachung.Server";
        private const string GITHUB_API_URL = "https://api.github.com/repos/{0}/{1}/releases/latest";

        public string CurrentVersion { get; set; } = ResolveCurrentVersion();
        public UpdateCheckResult? LastCheckResult { get; set; }
        public UpdateRuntimeStatus RuntimeStatus { get; }

        public GitHubUpdateService(
            HttpClient httpClient,
            ILogger<GitHubUpdateService> logger,
            ISettingsService settingsService)
        {
            _httpClient = httpClient;
            _logger = logger;
            _settingsService = settingsService;
            RuntimeStatus = new UpdateRuntimeStatus
            {
                CurrentVersion = CurrentVersion,
                LastMessage = "Updater bereit"
            };

            if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Einsatzueberwachung-Update-Checker");

            if (!_httpClient.DefaultRequestHeaders.Accept.Any())
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        public UpdateRuntimeStatus GetStatusSnapshot()
        {
            lock (_statusLock)
            {
                return RuntimeStatus.Clone();
            }
        }

        private void SetStatus(Action<UpdateRuntimeStatus> mutate)
        {
            lock (_statusLock)
            {
                mutate(RuntimeStatus);
            }
        }

        private static string ResolveCurrentVersion()
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null ? "0.0.0" : version.ToString(3);
        }

        private static string NormalizeVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var trimmed = raw.Trim().TrimStart('v', 'V');
            var match = Regex.Match(trimmed, @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?");
            if (!match.Success)
                return string.Empty;

            var major = match.Groups[1].Value;
            var minor = match.Groups[2].Success ? match.Groups[2].Value : "0";
            var patch = match.Groups[3].Success ? match.Groups[3].Value : "0";
            return $"{major}.{minor}.{patch}";
        }

        private static Version ParseVersionOrDefault(string version)
        {
            var normalized = NormalizeVersion(version);
            return Version.TryParse(normalized, out var parsed) ? parsed : new Version(0, 0, 0);
        }

        /// <summary>
        /// Linux-Paket (.zip/.tar.gz) passend zum Prozessor: linux-x64 bzw. linux-arm64. Ein falsches Paket wuerde
        /// eingespielt und der Dienst startete danach nicht mehr.
        /// </summary>
        public static bool IsLinuxAssetFor(string fileName, Architecture architecture)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var lower = fileName.ToLowerInvariant();
            var archive = lower.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                       || lower.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            if (!archive || !lower.Contains("linux"))
                return false;
            return architecture switch
            {
                Architecture.X64 => lower.Contains("x64") || lower.Contains("amd64"),
                Architecture.Arm64 => lower.Contains("arm64") || lower.Contains("aarch64"),
                _ => false,
            };
        }

        private static string ResolveExtensionFromUrl(string url)
        {
            if (url.Contains(".tar.gz", StringComparison.OrdinalIgnoreCase))
                return ".tar.gz";
            if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return ".zip";
            return ".bin";
        }

        private static string EscapeForBashSingleQuoted(string value)
            => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }
}

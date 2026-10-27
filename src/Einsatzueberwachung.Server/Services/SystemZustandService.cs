using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Security;
using Microsoft.Extensions.Options;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Zertifikat der oeffentlichen Adresse (Team-Handys, Traccar)</summary>
public sealed record ZertifikatZustand(string Host, DateTime? GueltigBisUtc, string? Fehler)
{
    public int? TageRest(DateTime nowUtc) => GueltigBisUtc is { } bis ? (int)Math.Floor((bis - nowUtc).TotalDays) : null;

    /// <summary>Abgelaufen, ungueltig oder in weniger als <see cref="SystemZustandService.ZertifikatWarnTage"/> Tagen faellig</summary>
    public bool Kritisch(DateTime nowUtc) => Fehler is not null || TageRest(nowUtc) is < SystemZustandService.ZertifikatWarnTage;
}

public sealed record SystemZustand(
    DateTime GeprueftUtc,
    ZertifikatZustand? Zertifikat,
    DateTime? LetztesBackup,
    long? FreierSpeicherBytes,
    bool? DiveraErreichbar,
    string AppZeitzone,
    string ServerZeitzone,
    DateTime AppZeit);

/// <summary>
/// Systemzustand fuer die Einsatzleitung (Einstellungen → System, Hinweis auf Startseite und /el): prueft alle
/// 6 Stunden (und auf Knopfdruck) das Zertifikat der oeffentlichen Adresse, Backup, Speicherplatz, Divera und
/// die Uhr. Ein ablaufendes Zertifikat faellt so Wochen vorher auf – nicht erst, wenn Traccar nichts mehr sendet.
/// </summary>
public sealed class SystemZustandService : BackgroundService
{
    public const int ZertifikatWarnTage = 14;
    public const long SpeicherWarnBytes = 1024L * 1024 * 1024;
    private static readonly TimeSpan Intervall = TimeSpan.FromHours(6);

    private readonly ISettingsService _settings;
    private readonly IDiveraService _divera;
    private readonly ITimeService _time;
    private readonly IWarningService _warnings;
    private readonly IOptionsMonitor<TeamMobileOptions> _teamMobileOptions;
    private readonly ILogger<SystemZustandService> _logger;
    private readonly SemaphoreSlim _pruefLock = new(1, 1);

    public SystemZustandService(
        ISettingsService settings,
        IDiveraService divera,
        ITimeService time,
        IWarningService warnings,
        IOptionsMonitor<TeamMobileOptions> teamMobileOptions,
        ILogger<SystemZustandService> logger)
    {
        _settings = settings;
        _divera = divera;
        _time = time;
        _warnings = warnings;
        _teamMobileOptions = teamMobileOptions;
        _logger = logger;
    }

    public SystemZustand? Aktuell { get; private set; }

    public event Action? Geaendert;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); // Start nicht ausbremsen
            while (!stoppingToken.IsCancellationRequested)
            {
                await PruefenAsync(stoppingToken);
                await Task.Delay(Intervall, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren
        }
    }

    public async Task<SystemZustand> PruefenAsync(CancellationToken cancellationToken = default)
    {
        await _pruefLock.WaitAsync(cancellationToken);
        try
        {
            var settings = await _settings.GetAppSettingsAsync();
            var nowUtc = DateTime.UtcNow;

            var host = OeffentlicherHost(settings.MobileBaseUrl, _teamMobileOptions.CurrentValue.PublicBaseUrl);
            var zertifikat = host is null ? null : await PruefeZertifikatAsync(host, cancellationToken);

            long? frei = null;
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(AppPathResolver.GetDataDirectory()));
                if (!string.IsNullOrEmpty(root)) frei = new DriveInfo(root).AvailableFreeSpace;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Freier Speicher nicht lesbar");
            }

            bool? divera = null;
            try
            {
                if (_divera.IsConfigured) divera = await _divera.TestConnectionAsync();
            }
            catch
            {
                divera = false;
            }

            var zustand = new SystemZustand(nowUtc, zertifikat, settings.LastBackupAt, frei, divera,
                string.IsNullOrWhiteSpace(settings.TimeZoneId) ? "(Server)" : settings.TimeZoneId,
                TimeZoneInfo.Local.Id, _time.Now);
            Aktuell = zustand;

            if (zertifikat is not null && zertifikat.Kritisch(nowUtc))
            {
                _warnings.AddWarning(new WarningEntry
                {
                    Title = "Zertifikat der öffentlichen Adresse",
                    Message = ZertifikatText(zertifikat, nowUtc),
                    Level = WarningLevel.Critical,
                    NavigationUrl = "/einstellungen?tab=system",
                    Source = WarningRuleDefinition.Sources.ServerCertificate,
                    Timestamp = _time.Now
                });
            }

            Geaendert?.Invoke();
            return zustand;
        }
        finally
        {
            _pruefLock.Release();
        }
    }

    public static string ZertifikatText(ZertifikatZustand z, DateTime nowUtc) =>
        z.Fehler is not null
            ? $"{z.Host}: {z.Fehler} – Traccar und Team-Handys verbinden sich nicht. Zertifikat erneuern (DEPLOYMENT.md)."
            : z.TageRest(nowUtc) is int tage && tage < 0
                ? $"{z.Host}: Zertifikat ist abgelaufen – Traccar und Team-Handys verbinden sich nicht."
                : $"{z.Host}: Zertifikat läuft in {z.TageRest(nowUtc)} Tagen ab ({z.GueltigBisUtc:dd.MM.yyyy}). Automatische Verlängerung prüfen (certbot renew --dry-run).";

    /// <summary>Host der oeffentlichen Adresse aus den Einstellungen (Vorrang) oder der Server-Konfiguration</summary>
    public static string? OeffentlicherHost(string? mobileBaseUrl, string? publicBaseUrl)
    {
        foreach (var kandidat in new[] { mobileBaseUrl, publicBaseUrl })
        {
            if (string.IsNullOrWhiteSpace(kandidat)) continue;
            var text = kandidat.Contains("://", StringComparison.Ordinal) ? kandidat : "https://" + kandidat;
            if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host))
                return uri.Host;
        }
        return null;
    }

    private static async Task<ZertifikatZustand> PruefeZertifikatAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, 443, timeout.Token);

            X509Certificate2? zertifikat = null;
            var fehler = SslPolicyErrors.None;
            X509ChainStatusFlags kette = X509ChainStatusFlags.NoError;
            using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, chain, errors) =>
            {
                if (cert is not null) zertifikat = new X509Certificate2(cert);
                fehler = errors;
                kette = chain?.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (a, s) => a | s.Status) ?? X509ChainStatusFlags.NoError;
                return true; // nur ansehen, nicht vertrauen
            });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, timeout.Token);

            var bis = zertifikat?.NotAfter.ToUniversalTime();
            string? text = fehler == SslPolicyErrors.None ? null
                : kette.HasFlag(X509ChainStatusFlags.NotTimeValid) ? "Zertifikat ist abgelaufen"
                : fehler.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) ? "Zertifikat passt nicht zur Adresse"
                : "Zertifikat ist ungültig";
            return new ZertifikatZustand(host, bis, text);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
            return new ZertifikatZustand(host, null, "Adresse nicht erreichbar (" + ex.GetType().Name + ")");
        }
    }
}

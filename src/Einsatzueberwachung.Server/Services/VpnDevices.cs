using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Angaben des WireGuard-Servers aus <c>einsatz-wg-peer info</c>.</summary>
public sealed record VpnServerInfo(string PublicKey, int ListenPort, string AddressCidr, bool DnsEnabled)
{
    /// <summary>Adresse des Servers im VPN (z.B. 10.10.0.1).</summary>
    public string ServerIp => AddressCidr.Split('/')[0];

    /// <summary>Netz, das Geraete durch den Tunnel schicken (z.B. 10.10.0.0/24) – nur das, nicht den ganzen Verkehr.</summary>
    public string Network
    {
        get
        {
            var parts = AddressCidr.Split('/');
            var bytes = IPAddress.Parse(parts[0]).GetAddressBytes();
            var prefix = int.Parse(parts[1]);
            for (var i = 0; i < 4; i++)
            {
                var keep = Math.Clamp(prefix - i * 8, 0, 8);
                bytes[i] &= (byte)(0xFF << (8 - keep));
            }
            return $"{new IPAddress(bytes)}/{prefix}";
        }
    }
}

public sealed record VpnNewPeer(string Ip, string PublicKey, string PrivateKey);

public sealed record VpnPeerStatus(string PublicKey, string Ip, DateTime? LastHandshakeUtc);

/// <summary>Zugriff auf WireGuard ueber das root-Hilfsprogramm (fuer Tests austauschbar).</summary>
public interface IVpnPeerTool
{
    bool IsAvailable { get; }
    Task<VpnServerInfo?> InfoAsync();
    Task<VpnNewPeer> AddAsync();
    Task RemoveAsync(string publicKey);
    Task<IReadOnlyList<VpnPeerStatus>> StatusAsync();
}

/// <summary>
/// Ruft <c>sudo -n /usr/local/sbin/einsatz-wg-peer</c> auf. Das Programm gehoert root und ist fuer den Dienst nicht
/// beschreibbar; eingerichtet von deploy/scripts/setup-vpn.sh.
/// </summary>
public sealed class SudoVpnPeerTool(ILogger<SudoVpnPeerTool> logger) : IVpnPeerTool
{
    public const string ToolPath = "/usr/local/sbin/einsatz-wg-peer";

    public bool IsAvailable => OperatingSystem.IsLinux() && File.Exists(ToolPath);

    public async Task<VpnServerInfo?> InfoAsync()
    {
        if (!IsAvailable) return null;
        try
        {
            var values = ParseKeyValues(await RunAsync("info"));
            return new VpnServerInfo(values["public_key"], int.Parse(values["listen_port"]), values["address"],
                values.TryGetValue("dns", out var dns) && dns == "yes");
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            logger.LogWarning(ex, "VPN-Angaben konnten nicht gelesen werden");
            return null;
        }
    }

    public async Task<VpnNewPeer> AddAsync()
    {
        var values = ParseKeyValues(await RunAsync("add"));
        return new VpnNewPeer(values["ip"], values["public_key"], values["private_key"]);
    }

    public Task RemoveAsync(string publicKey) => RunAsync("remove", publicKey);

    public async Task<IReadOnlyList<VpnPeerStatus>> StatusAsync()
    {
        var output = await RunAsync("status");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(f => f.Length >= 3)
            .Select(f => new VpnPeerStatus(f[0], f[1],
                long.TryParse(f[2], out var unix) && unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : null))
            .ToList();
    }

    private static async Task<string> RunAsync(params string[] args)
    {
        var info = new ProcessStartInfo("sudo") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-n");
        info.ArgumentList.Add(ToolPath);
        foreach (var arg in args) info.ArgumentList.Add(arg);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Hilfsprogramm nicht startbar.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"VPN-Hilfsprogramm meldet einen Fehler: {(await stderr).Trim()}");
        return await stdout;
    }

    internal static Dictionary<string, string> ParseKeyValues(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
}

/// <summary>Name, Ablaufdatum und Ersteller je VPN-Geraet. Private Schluessel werden nie gespeichert.</summary>
public sealed class VpnDeviceStore
{
    public const string FileName = "vpn-geraete.json";
    private readonly string _filePath;
    private readonly object _lock = new();
    private Data _data;

    public VpnDeviceStore(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, FileName);
        _data = File.Exists(_filePath)
            ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_filePath)) ?? new Data()
            : new Data();
    }

    public static VpnDeviceStore ForDataDirectory() => new(AppPathResolver.GetDataDirectory());

    public string Endpoint { get { lock (_lock) return _data.Endpoint; } }

    public IReadOnlyList<VpnDevice> Devices { get { lock (_lock) return _data.Devices.ToList(); } }

    public void SetEndpoint(string endpoint)
    {
        lock (_lock) { _data.Endpoint = endpoint.Trim(); Save(); }
    }

    public void Add(VpnDevice device)
    {
        lock (_lock) { _data.Devices.Add(device); Save(); }
    }

    public void Remove(string publicKey)
    {
        lock (_lock) { _data.Devices.RemoveAll(d => d.PublicKey == publicKey); Save(); }
    }

    private void Save()
    {
        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _filePath, overwrite: true);
    }

    private sealed class Data
    {
        public string Endpoint { get; set; } = string.Empty;
        public List<VpnDevice> Devices { get; set; } = [];
    }
}

public sealed class VpnDevice
{
    public string Name { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}

/// <summary>Neues Geraet: Konfiguration (mit privatem Schluessel) nur fuer die einmalige Anzeige als QR-Code.</summary>
public sealed record VpnDeviceCreated(VpnDevice Device, string ClientConfig);

/// <summary>VPN-Geraete anlegen, sperren und abgelaufene automatisch entfernen (1.10).</summary>
public sealed class VpnDeviceService(IVpnPeerTool tool, VpnDeviceStore store, IAuditLogService audit)
{
    public const int MaxNameLength = 40;

    public bool IsAvailable => tool.IsAvailable;

    public Task<VpnServerInfo?> GetServerInfoAsync() => tool.InfoAsync();

    public Task<IReadOnlyList<VpnPeerStatus>> GetStatusAsync() => tool.StatusAsync();

    public async Task<VpnDeviceCreated> CreateAsync(string name, DateTime? expiresUtc, DateTime nowUtc)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > MaxNameLength || name.Any(char.IsControl))
            throw new ArgumentException($"Bitte einen Gerätenamen mit höchstens {MaxNameLength} Zeichen angeben.");
        if (string.IsNullOrWhiteSpace(store.Endpoint))
            throw new InvalidOperationException("Bitte zuerst die Adresse des VPN-Servers (Domain:Port) eintragen.");

        var server = await tool.InfoAsync() ?? throw new InvalidOperationException("WireGuard auf dem Server nicht erreichbar.");
        var peer = await tool.AddAsync();
        var device = new VpnDevice { Name = name, PublicKey = peer.PublicKey, Ip = peer.Ip, CreatedUtc = nowUtc, ExpiresUtc = expiresUtc };
        store.Add(device);
        audit.Log("VPN-Gerät angelegt", $"{name} ({peer.Ip})" + (expiresUtc is { } e ? $", gültig bis {e.ToLocalTime():dd.MM.yyyy HH:mm}" : ""));

        return new VpnDeviceCreated(device, BuildClientConfig(peer, server, store.Endpoint));
    }

    public async Task RemoveAsync(string publicKey, string reason = "gesperrt")
    {
        var device = store.Devices.FirstOrDefault(d => d.PublicKey == publicKey);
        await tool.RemoveAsync(publicKey);
        store.Remove(publicKey);
        audit.Log("VPN-Gerät " + reason, device is null ? publicKey : $"{device.Name} ({device.Ip})");
    }

    public async Task<int> RemoveExpiredAsync(DateTime nowUtc)
    {
        var expired = store.Devices.Where(d => d.ExpiresUtc is { } e && e <= nowUtc).ToList();
        foreach (var device in expired)
        {
            await RemoveAsync(device.PublicKey, "abgelaufen");
        }
        return expired.Count;
    }

    /// <summary>Konfiguration fuer die WireGuard-App: nur das VPN-Netz durch den Tunnel, DNS nur wenn eingerichtet.</summary>
    public static string BuildClientConfig(VpnNewPeer peer, VpnServerInfo server, string endpoint)
    {
        var lines = new List<string> { "[Interface]", $"PrivateKey = {peer.PrivateKey}", $"Address = {peer.Ip}/32" };
        if (server.DnsEnabled) lines.Add($"DNS = {server.ServerIp}");
        lines.AddRange(
        [
            "",
            "[Peer]",
            $"PublicKey = {server.PublicKey}",
            $"Endpoint = {endpoint}",
            $"AllowedIPs = {server.Network}",
            "PersistentKeepalive = 25",
        ]);
        return string.Join("\n", lines) + "\n";
    }
}

/// <summary>Entfernt abgelaufene VPN-Geraete (z.B. „nur dieses Wochenende“) alle fuenf Minuten.</summary>
public sealed class VpnDeviceCleanupService(VpnDeviceService devices, ILogger<VpnDeviceCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!devices.IsAvailable) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try
            {
                var removed = await devices.RemoveExpiredAsync(DateTime.UtcNow);
                if (removed > 0) logger.LogInformation("{Count} abgelaufene VPN-Geräte entfernt", removed);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Abgelaufene VPN-Geräte konnten nicht entfernt werden");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

// ------------------------------------------------------------
// 1.10 – VPN-Geräte per QR-Code
// ------------------------------------------------------------
public sealed class VpnDeviceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-vpn-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTool _tool = new();
    private readonly FakeAudit _audit = new();

    public VpnDeviceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private (VpnDeviceService Service, VpnDeviceStore Store) Create(string endpoint = "dein-einsatz.mywire.org:51820")
    {
        var store = new VpnDeviceStore(_dir);
        if (endpoint.Length > 0) store.SetEndpoint(endpoint);
        return (new VpnDeviceService(_tool, store, _audit), store);
    }

    [Fact]
    public async Task NeuesGeraet_KonfigurationFuerWireGuard_MitDnsNurWennEingerichtet()
    {
        var (service, _) = Create();

        _tool.Dns = true;
        var withDns = await service.CreateAsync("Tablet EL", null, DateTime.UtcNow);
        _tool.Dns = false;
        var withoutDns = await service.CreateAsync("Laptop ELW", null, DateTime.UtcNow);

        Assert.Equal("""
            [Interface]
            PrivateKey = PRIV-1
            Address = 10.10.0.11/32
            DNS = 10.10.0.1

            [Peer]
            PublicKey = SERVERKEY=
            Endpoint = dein-einsatz.mywire.org:51820
            AllowedIPs = 10.10.0.0/24
            PersistentKeepalive = 25

            """.Replace("\r\n", "\n"), withDns.ClientConfig);
        Assert.DoesNotContain("DNS", withoutDns.ClientConfig);
        Assert.Contains("Address = 10.10.0.12/32", withoutDns.ClientConfig);
    }

    [Fact]
    public async Task PrivaterSchluessel_WirdNichtGespeichert_NameUndAblaufSchon()
    {
        var (service, store) = Create();
        var expires = new DateTime(2026, 10, 27, 18, 0, 0, DateTimeKind.Utc);

        await service.CreateAsync("Gast-Tablet Übung", expires, DateTime.UtcNow);

        var file = File.ReadAllText(Path.Combine(_dir, VpnDeviceStore.FileName));
        Assert.DoesNotContain("PRIV-", file);
        var reloaded = new VpnDeviceStore(_dir).Devices.Single();
        Assert.Equal("Gast-Tablet Übung", reloaded.Name);
        Assert.Equal("10.10.0.11", reloaded.Ip);
        Assert.Equal(expires, reloaded.ExpiresUtc);
        Assert.Contains(_audit.Logged, e => e.StartsWith("VPN-Gerät angelegt") && e.Contains("Gast-Tablet Übung"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Name mit\u0007Steuerzeichen")]
    [InlineData("Ein sehr langer Gerätename der über vierzig Zeichen hat")]
    public async Task UngueltigerName_WirdAbgelehnt_OhneGeraetAnzulegen(string name)
    {
        var (service, store) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(name, null, DateTime.UtcNow));
        Assert.Empty(store.Devices);
        Assert.Equal(0, _tool.AddCalls);
    }

    [Fact]
    public async Task OhneServerAdresse_KeinGeraet()
    {
        var (service, _) = Create(endpoint: "");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("Tablet", null, DateTime.UtcNow));
        Assert.Equal(0, _tool.AddCalls);
    }

    [Fact]
    public async Task Sperren_EntferntPeerUndEintrag()
    {
        var (service, store) = Create();
        var created = await service.CreateAsync("Tablet", null, DateTime.UtcNow);

        await service.RemoveAsync(created.Device.PublicKey);

        Assert.Contains(created.Device.PublicKey, _tool.Removed);
        Assert.Empty(store.Devices);
        Assert.Contains(_audit.Logged, e => e.StartsWith("VPN-Gerät gesperrt"));
    }

    [Fact]
    public async Task AbgelaufeneGeraete_WerdenAutomatischEntfernt()
    {
        var (service, store) = Create();
        var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        var expired = await service.CreateAsync("Gast", now.AddDays(1), now);
        var permanent = await service.CreateAsync("Tablet EL", null, now);
        var later = await service.CreateAsync("Übung", now.AddDays(3), now);

        var removed = await service.RemoveExpiredAsync(now.AddDays(2));

        Assert.Equal(1, removed);
        Assert.Equal([expired.Device.PublicKey], _tool.Removed);
        Assert.Equal(["Tablet EL", "Übung"], store.Devices.Select(d => d.Name).Order());
        Assert.Contains(_audit.Logged, e => e.StartsWith("VPN-Gerät abgelaufen") && e.Contains("Gast"));
    }

    [Theory]
    [InlineData("10.10.0.1/24", "10.10.0.0/24")]
    [InlineData("10.8.3.1/16", "10.8.0.0/16")]
    [InlineData("192.168.77.1/24", "192.168.77.0/24")]
    public void NetzWirdAusServerAdresseBerechnet(string cidr, string network) =>
        Assert.Equal(network, new VpnServerInfo("K=", 51820, cidr, false).Network);

    [Fact]
    public void AusgabeDesHilfsprogramms_WirdGelesen()
    {
        var values = SudoVpnPeerTool.ParseKeyValues("ip=10.10.0.11\npublic_key=abc+/=\nprivate_key=xyz=\n");
        Assert.Equal("10.10.0.11", values["ip"]);
        Assert.Equal("abc+/=", values["public_key"]);
        Assert.Equal("xyz=", values["private_key"]);
    }

    private sealed class FakeTool : IVpnPeerTool
    {
        private int _next = 11;
        public bool Dns { get; set; }
        public int AddCalls { get; private set; }
        public List<string> Removed { get; } = [];
        public bool IsAvailable => true;

        public Task<VpnServerInfo?> InfoAsync() => Task.FromResult<VpnServerInfo?>(new("SERVERKEY=", 51820, "10.10.0.1/24", Dns));

        public Task<VpnNewPeer> AddAsync()
        {
            AddCalls++;
            var n = _next++;
            return Task.FromResult(new VpnNewPeer($"10.10.0.{n}", $"PUB-{n - 10}=", $"PRIV-{n - 10}"));
        }

        public Task RemoveAsync(string publicKey)
        {
            Removed.Add(publicKey);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VpnPeerStatus>> StatusAsync() => Task.FromResult<IReadOnlyList<VpnPeerStatus>>([]);
    }

    private sealed class FakeAudit : IAuditLogService
    {
        public List<string> Logged { get; } = [];
        public IReadOnlyList<AuditLogEntry> Entries => [];
        public void Log(string aktion, string details, string? teamName = null) => Logged.Add($"{aktion}: {details}");
        public void Clear() => Logged.Clear();
        public Task<List<AuditLogEntry>> GetEntriesAsync(int maxCount = 500) => Task.FromResult(new List<AuditLogEntry>());
    }
}

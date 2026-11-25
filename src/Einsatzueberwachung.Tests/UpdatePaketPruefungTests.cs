using System.Net;
using System.Security.Cryptography;
using System.Text;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Update-Pakete nur mit gültiger Signatur des Release-Schlüssels installieren (Issue 211)</summary>
public class UpdatePaketPruefungTests
{
    // Mit dem echten Release-Schlüssel per openssl signiert (openssl dgst -sha256 -sign), 18.11.2026
    private static readonly byte[] Testpaket = Encoding.ASCII.GetBytes("EinsatzSpur Testpaket 211\n");
    private const string TestSignatur = "MEUCIETyQmxsrVipAyRY+VIqTBYmEjFYaUfx4yTMOHQKQzfdAiEA+PKeWvyXx/nzT21guSmW6KEUE6q1qJHnmnHlep2Sirs=";

    [Fact]
    public void Signatur_VonOpenssl_PasstZumEingebautenSchluessel()
    {
        Assert.True(UpdatePaketPruefung.SignaturGueltig(Testpaket, Convert.FromBase64String(TestSignatur)));
    }

    [Fact]
    public void Signatur_VeraendertesPaketLeereOderFremdeSignatur_Ungueltig()
    {
        var signatur = Convert.FromBase64String(TestSignatur);
        var veraendert = (byte[])Testpaket.Clone();
        veraendert[0] ^= 1;
        Assert.False(UpdatePaketPruefung.SignaturGueltig(veraendert, signatur));
        Assert.False(UpdatePaketPruefung.SignaturGueltig(Testpaket, []));
        Assert.False(UpdatePaketPruefung.SignaturGueltig(Testpaket, Encoding.ASCII.GetBytes("keine Signatur")));

        // Mit einem fremden Schlüssel korrekt signiert: trotzdem abgelehnt
        using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fremdeSignatur = fremd.SignData(Testpaket, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.False(UpdatePaketPruefung.SignaturGueltig(Testpaket, fremdeSignatur));
        Assert.True(UpdatePaketPruefung.SignaturGueltig(Testpaket, fremdeSignatur, [Convert.ToBase64String(fremd.ExportSubjectPublicKeyInfo())]));
    }

    [Fact]
    public void Pruefsumme_ImFormatVonSha256sum()
    {
        var hex = Convert.ToHexString(SHA256.HashData(Testpaket)).ToLowerInvariant();
        Assert.True(UpdatePaketPruefung.PruefsummeGueltig(Testpaket, Encoding.ASCII.GetBytes(hex + "  linux-x64-server-1.18.0.zip\n")));
        Assert.True(UpdatePaketPruefung.PruefsummeGueltig(Testpaket, Encoding.ASCII.GetBytes(hex.ToUpperInvariant())));
        Assert.False(UpdatePaketPruefung.PruefsummeGueltig(Testpaket, Encoding.ASCII.GetBytes(new string('0', 64))));
        Assert.False(UpdatePaketPruefung.PruefsummeGueltig(Testpaket, Encoding.ASCII.GetBytes("kaputt")));
    }
}

[CollectionDefinition(nameof(UpdateInstallationPrueftSignaturTests), DisableParallelization = true)]
public class UpdateInstallationKollektion { }

/// <summary>Installation bricht vor dem Entpacken ab, wenn Signatur fehlt oder nicht passt</summary>
[Collection(nameof(UpdateInstallationPrueftSignaturTests))]
public sealed class UpdateInstallationPrueftSignaturTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-update-sig-" + Guid.NewGuid().ToString("N"));
    private readonly string? _vorher = Environment.GetEnvironmentVariable("EINSATZUEBERWACHUNG_DATA_DIR");

    public UpdateInstallationPrueftSignaturTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("EINSATZUEBERWACHUNG_DATA_DIR", _dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("EINSATZUEBERWACHUNG_DATA_DIR", _vorher);
        Directory.Delete(_dir, recursive: true);
    }

    private sealed class FalschesGitHub(Dictionary<string, byte[]> dateien) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            return Task.FromResult(dateien.TryGetValue(url, out var inhalt)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(inhalt) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static (GitHubUpdateService Dienst, List<string> Audit) Dienst(bool mitSignatur, byte[] signatur)
    {
        const string api = "https://api.github.com/repos/Elemirus1996/Einsatzueberwachung.Server/releases/latest";
        const string zip = "https://github.com/x/releases/download/v99.0.0/linux-x64-server-99.0.0.zip";
        var paket = Encoding.ASCII.GetBytes("kein echtes zip");
        var assets = $"{{\"name\":\"linux-x64-server-99.0.0.zip\",\"browser_download_url\":\"{zip}\"}}," +
                     $"{{\"name\":\"linux-x64-server-99.0.0.zip.sha256\",\"browser_download_url\":\"{zip}.sha256\"}}" +
                     (mitSignatur ? $",{{\"name\":\"linux-x64-server-99.0.0.zip.sig\",\"browser_download_url\":\"{zip}.sig\"}}" : "");
        var release = $"{{\"tag_name\":\"v99.0.0\",\"html_url\":\"https://github.com/x\",\"body\":\"\",\"assets\":[{assets}]}}";
        var dateien = new Dictionary<string, byte[]>
        {
            [api] = Encoding.UTF8.GetBytes(release),
            [zip] = paket,
            [zip + ".sha256"] = Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(paket)) + "  linux-x64-server-99.0.0.zip"),
            [zip + ".sig"] = signatur
        };
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) => m.Name switch
        {
            nameof(ISettingsService.GetAppSettingsAsync) => Task.FromResult(new AppSettings()),
            _ => null
        });
        var audit = new List<string>();
        var log = EinsatzAbschlussTests.Recorder<IAuditLogService>.Create((m, a) =>
        {
            if (m.Name == nameof(IAuditLogService.Log)) audit.Add($"{a![0]}: {a[1]}");
            return null;
        });
        return (new GitHubUpdateService(new HttpClient(new FalschesGitHub(dateien)), NullLogger<GitHubUpdateService>.Instance, settings.Proxy, log.Proxy), audit);
    }

    [Fact]
    public async Task OhneSignatur_KeineInstallation()
    {
        var (dienst, audit) = Dienst(mitSignatur: false, []);
        var ergebnis = await dienst.InstallLatestAsync();
        Assert.False(ergebnis.Success);
        Assert.Contains("nicht signiert", ergebnis.Message);
        Assert.Contains(audit, e => e.StartsWith("Update abgelehnt"));
        Assert.False(Directory.Exists(Path.Combine(_dir, "updates", "releases", "99.0.0")));
    }

    [Fact]
    public async Task FalscheSignatur_KeineInstallation()
    {
        using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (dienst, _) = Dienst(mitSignatur: true, fremd.SignData(Encoding.ASCII.GetBytes("kein echtes zip"), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var ergebnis = await dienst.InstallLatestAsync();
        Assert.False(ergebnis.Success);
        Assert.Contains("Signatur", ergebnis.Message);
        Assert.False(Directory.Exists(Path.Combine(_dir, "updates", "releases", "99.0.0")));
    }
}

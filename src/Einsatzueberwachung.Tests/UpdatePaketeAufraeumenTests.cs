using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Alte Update-Pakete in data/updates werden vor dem naechsten Update entfernt (#181: 7,6 GB)</summary>
public sealed class UpdatePaketeAufraeumenTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eu-updates-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void AltePaketeEntfernen_LoeschtDownloadsUndEntpackte_FehlendeOrdnerOk()
    {
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((_, _) => null);
        using var http = new HttpClient();
        var dienst = new GitHubUpdateService(http, NullLogger<GitHubUpdateService>.Instance, settings.Proxy);

        Directory.CreateDirectory(_dir);
        dienst.AltePaketeEntfernen(_dir); // ohne downloads/ und releases/ kein Fehler

        Directory.CreateDirectory(Path.Combine(_dir, "downloads"));
        File.WriteAllText(Path.Combine(_dir, "downloads", "1.13.0.zip"), "x");
        File.WriteAllText(Path.Combine(_dir, "downloads", "1.14.0.zip"), "x");
        Directory.CreateDirectory(Path.Combine(_dir, "releases", "1.13.0", "wwwroot"));
        File.WriteAllText(Path.Combine(_dir, "releases", "1.13.0", "wwwroot", "app.js"), "x");
        File.WriteAllText(Path.Combine(_dir, "andere.txt"), "bleibt");

        dienst.AltePaketeEntfernen(_dir);

        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_dir, "downloads")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_dir, "releases")));
        Assert.True(File.Exists(Path.Combine(_dir, "andere.txt")));
    }
}

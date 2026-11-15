using Einsatzueberwachung.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Speicherverwaltung in Einstellungen → Wartung (Vorfall 10.11.2026: Platte durch Backups voll)</summary>
public sealed class SpeicherVerwaltungTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eu-speicher-" + Guid.NewGuid().ToString("N"));
    private readonly SpeicherPfade _pfade;
    private readonly SpeicherVerwaltungService _svc;

    public SpeicherVerwaltungTests()
    {
        _pfade = new SpeicherPfade(
            Path.Combine(_root, "backups"),
            Path.Combine(_root, "updates"),
            Path.Combine(_root, "data", "updates"));
        Directory.CreateDirectory(_pfade.BackupVerzeichnis);
        Directory.CreateDirectory(_pfade.UpdateVerzeichnis);
        _svc = new SpeicherVerwaltungService(_pfade, NullLogger<SpeicherVerwaltungService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* egal */ }
    }

    private string Datei(string verzeichnis, string name, int bytes = 10, DateTime? geaendertUtc = null)
    {
        var pfad = Path.Combine(verzeichnis, name);
        Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
        File.WriteAllBytes(pfad, new byte[bytes]);
        if (geaendertUtc is { } t) File.SetLastWriteTimeUtc(pfad, t);
        return pfad;
    }

    private string Ordner(string verzeichnis, string name, DateTime geaendertUtc, int bytes = 5)
    {
        var pfad = Path.Combine(verzeichnis, name);
        Datei(pfad, "inhalt.bin", bytes);
        Directory.SetLastWriteTimeUtc(pfad, geaendertUtc);
        return pfad;
    }

    [Fact]
    public async Task NeuestesBackupUndLaufendesTmpSindGeschuetzt()
    {
        var b = _pfade.BackupVerzeichnis;
        Datei(b, "einsatz-backup-20261108-033002.tar.gz", 100);
        Datei(b, "einsatz-backup-20261109-033001.tar.gz", 200);
        Datei(b, "fremd.txt");
        Ordner(b, "tmp-20261101-033001", DateTime.UtcNow.AddDays(-9));
        Ordner(b, "tmp-20261110-033001", DateTime.UtcNow.AddMinutes(-10));

        var liste = (await _svc.AuflistenAsync()).Eintraege.Where(e => e.Bereich == SpeicherBereich.ServerBackups).ToList();

        Assert.Equal(4, liste.Count); // fremd.txt taucht nicht auf
        Assert.True(liste.Single(e => e.Name == "einsatz-backup-20261109-033001.tar.gz").Geschuetzt);
        Assert.False(liste.Single(e => e.Name == "einsatz-backup-20261108-033002.tar.gz").Geschuetzt);
        Assert.False(liste.Single(e => e.Name == "tmp-20261101-033001").Geschuetzt);
        Assert.True(liste.Single(e => e.Name == "tmp-20261110-033001").Geschuetzt);
        Assert.Equal(200, liste.Single(e => e.Name == "einsatz-backup-20261109-033001.tar.gz").Bytes);
    }

    [Fact]
    public async Task LoeschtNurAusgewaehlteUndUngeschuetzte()
    {
        var b = _pfade.BackupVerzeichnis;
        var alt = Datei(b, "einsatz-backup-20261107-033009.tar.gz", 100);
        var neu = Datei(b, "einsatz-backup-20261109-033001.tar.gz", 100);
        var tmp = Ordner(b, "tmp-20261101-033001", DateTime.UtcNow.AddDays(-9), 50);

        var ergebnis = await _svc.LoeschenAsync([
            (SpeicherBereich.ServerBackups, "einsatz-backup-20261107-033009.tar.gz"),
            (SpeicherBereich.ServerBackups, "tmp-20261101-033001"),
            (SpeicherBereich.ServerBackups, "einsatz-backup-20261109-033001.tar.gz"),
        ]);

        Assert.Equal(2, ergebnis.Geloescht);
        Assert.Equal(150, ergebnis.FreigegebenBytes);
        Assert.Single(ergebnis.Fehler);
        Assert.False(File.Exists(alt));
        Assert.False(Directory.Exists(tmp));
        Assert.True(File.Exists(neu));
    }

    [Theory]
    [InlineData(SpeicherBereich.ServerBackups, "../updates/linux-x64-server-1.10.0.zip")]
    [InlineData(SpeicherBereich.ServerBackups, "fremd.txt")]
    [InlineData(SpeicherBereich.UpdatePakete, "current-version.txt")]
    [InlineData(SpeicherBereich.UpdatePakete, "einsatz-backup-20261107-033009.tar.gz")]
    [InlineData(SpeicherBereich.UpdateDownloads, "/etc/passwd")]
    public async Task FremdeOderUngueltigeNamenWerdenNichtGeloescht(SpeicherBereich bereich, string name)
    {
        var dateien = new[]
        {
            Datei(_pfade.BackupVerzeichnis, "einsatz-backup-20261107-033009.tar.gz"),
            Datei(_pfade.BackupVerzeichnis, "einsatz-backup-20261109-033001.tar.gz"),
            Datei(_pfade.BackupVerzeichnis, "fremd.txt"),
            Datei(_pfade.UpdateVerzeichnis, "linux-x64-server-1.10.0.zip"),
            Datei(_pfade.UpdateVerzeichnis, "current-version.txt"),
        };

        var ergebnis = await _svc.LoeschenAsync([(bereich, name)]);

        Assert.Equal(0, ergebnis.Geloescht);
        Assert.Single(ergebnis.Fehler);
        Assert.All(dateien, d => Assert.True(File.Exists(d)));
    }

    [Fact]
    public async Task UpdateResteNeuesterRollbackGeschuetztSteuerdateienUnsichtbar()
    {
        var u = _pfade.UpdateVerzeichnis;
        Ordner(u, "work-1.13.0", DateTime.UtcNow.AddDays(-10));
        Ordner(u, "backup-1.12.0", DateTime.UtcNow.AddDays(-20));
        Ordner(u, "backup-1.13.0", DateTime.UtcNow.AddDays(-10));
        Datei(u, "linux-x64-server-1.10.1.zip");
        Datei(u, "last-package.zip");
        Datei(u, "current-version.txt");
        Datei(u, "update.lock", 0);

        var liste = (await _svc.AuflistenAsync()).Eintraege.Where(e => e.Bereich == SpeicherBereich.UpdatePakete).ToList();

        Assert.Equal(["backup-1.12.0", "backup-1.13.0", "linux-x64-server-1.10.1.zip", "work-1.13.0"], liste.Select(e => e.Name).Order());
        Assert.True(liste.Single(e => e.Name == "backup-1.13.0").Geschuetzt);
        Assert.False(liste.Single(e => e.Name == "backup-1.12.0").Geschuetzt);
        Assert.False(liste.Single(e => e.Name == "work-1.13.0").Geschuetzt);
    }

    [Fact]
    public async Task HeruntergeladeneUpdatesWerdenMitUnterordnerGelistetUndGeloescht()
    {
        var d = _pfade.UpdateDownloadVerzeichnis;
        var paket = Datei(Path.Combine(d, "downloads"), "1.13.0.zip", 30);
        var release = Ordner(Path.Combine(d, "releases"), "1.13.0", DateTime.UtcNow.AddDays(-5), 20);

        var liste = (await _svc.AuflistenAsync()).Eintraege.Where(e => e.Bereich == SpeicherBereich.UpdateDownloads).ToList();
        Assert.Equal(["downloads/1.13.0.zip", "releases/1.13.0"], liste.Select(e => e.Name).Order());

        var ergebnis = await _svc.LoeschenAsync(liste.Select(e => (e.Bereich, e.Name)));

        Assert.Equal(2, ergebnis.Geloescht);
        Assert.False(File.Exists(paket));
        Assert.False(Directory.Exists(release));
    }

    [Fact]
    public async Task FehlendeVerzeichnisseSindLeerOhneFehler()
    {
        var svc = new SpeicherVerwaltungService(
            new SpeicherPfade(Path.Combine(_root, "gibts-nicht-1"), Path.Combine(_root, "gibts-nicht-2"), Path.Combine(_root, "gibts-nicht-3")),
            NullLogger<SpeicherVerwaltungService>.Instance);

        var uebersicht = await svc.AuflistenAsync();

        Assert.Empty(uebersicht.Eintraege);
        Assert.Empty(uebersicht.Fehler);
        Assert.False(Directory.Exists(Path.Combine(_root, "gibts-nicht-1"))); // wird nicht angelegt
    }

    [Theory]
    [InlineData(50L * 1024 * 1024 * 1024, 118L * 1024 * 1024 * 1024, false)] // 58 % belegt
    [InlineData(10L * 1024 * 1024 * 1024, 118L * 1024 * 1024 * 1024, true)]  // 92 % belegt
    [InlineData(500L * 1024 * 1024, 2000L * 1024 * 1024 * 1024, true)]        // < 1 GB frei
    [InlineData(0L, 118L * 1024 * 1024 * 1024, true)]                         // Vorfall 10.11.
    public void SystemzustandWarntBeiKnappemSpeicher(long frei, long gesamt, bool warnt) =>
        Assert.Equal(warnt, SystemZustandService.SpeicherKnapp(frei, gesamt) is not null);

    [Fact]
    public void SystemzustandOhneSpeicherangabeWarntNicht() =>
        Assert.Null(SystemZustandService.SpeicherKnapp(null, null));

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(5_905_580_032L, "5.5 GB")]
    public void GroesseWirdLesbarFormatiert(long bytes, string erwartet) =>
        Assert.Equal(erwartet, SpeicherVerwaltungService.FormatGroesse(bytes).Replace(',', '.'));
}

using System.Text.RegularExpressions;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Bereiche, die in Einstellungen → Wartung aufgeraeumt werden koennen</summary>
public enum SpeicherBereich
{
    /// <summary>Naechtliche Server-Backups von backup.sh (/opt/einsatzueberwachung/backups)</summary>
    ServerBackups,

    /// <summary>Arbeits- und Rollback-Ordner von apply-update.sh (/opt/einsatzueberwachung/updates)</summary>
    UpdatePakete,

    /// <summary>Heruntergeladene und entpackte Updates der App (data/updates) – landen sonst in jedem Backup</summary>
    UpdateDownloads,
}

public sealed record SpeicherEintrag(
    SpeicherBereich Bereich,
    string Name,
    long Bytes,
    DateTime GeaendertUtc,
    bool IstOrdner,
    bool Geschuetzt,
    string? Hinweis);

public sealed record SpeicherUebersicht(
    long? GesamtBytes,
    long? FreiBytes,
    IReadOnlyList<SpeicherEintrag> Eintraege,
    IReadOnlyDictionary<SpeicherBereich, string> Fehler)
{
    public double? BelegtProzent => GesamtBytes is > 0 && FreiBytes is long frei
        ? 100d * (GesamtBytes.Value - frei) / GesamtBytes.Value
        : null;
}

public sealed record SpeicherLoeschErgebnis(int Geloescht, long FreigegebenBytes, IReadOnlyList<string> Fehler);

/// <summary>Verzeichnisse der Speicherverwaltung. Standard: neben dem Datenverzeichnis (Linux-Installation unter /opt/einsatzueberwachung).</summary>
public sealed record SpeicherPfade(string BackupVerzeichnis, string UpdateVerzeichnis, string UpdateDownloadVerzeichnis)
{
    public const string BackupDirEnv = "EINSATZUEBERWACHUNG_BACKUP_DIR";
    public const string UpdateDirEnv = "EINSATZUEBERWACHUNG_UPDATE_DIR";

    public static SpeicherPfade AusUmgebung()
    {
        var daten = Path.GetFullPath(AppPathResolver.GetDataDirectory());
        var appRoot = Directory.GetParent(daten.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName ?? daten;

        static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } wert ? wert : null;

        return new SpeicherPfade(
            Env(BackupDirEnv) ?? Path.Combine(appRoot, "backups"),
            Env(UpdateDirEnv) ?? Path.Combine(appRoot, "updates"),
            Path.Combine(daten, "updates"));
    }
}

/// <summary>
/// Listet Server-Backups und Update-Reste auf und loescht ausgewaehlte Eintraege (Vorfall 10.11.2026: Platte voll,
/// Server startete nicht mehr). Geloescht wird nur, was die Auflistung selbst liefert – Namen aus dem Browser werden
/// nie zu Pfaden zusammengesetzt. Das neueste Backup und der neueste Rollback-Stand sind geschuetzt.
/// </summary>
public sealed partial class SpeicherVerwaltungService(SpeicherPfade pfade, ILogger<SpeicherVerwaltungService> logger)
{
    /// <summary>tmp-Ordner von backup.sh, die juenger sind, gehoeren evtl. zu einem laufenden Backup</summary>
    public static readonly TimeSpan LaufendesBackupFenster = TimeSpan.FromHours(3);

    public const double WarnProzent = 85;
    public const double KritischProzent = 95;

    [GeneratedRegex(@"^einsatz-backup-\d{8}-\d{6}\.tar\.gz$")] private static partial Regex BackupDatei();
    [GeneratedRegex(@"^tmp-\d{8}-\d{6}$")] private static partial Regex BackupTmpOrdner();
    [GeneratedRegex(@"^work-[A-Za-z0-9._-]+$")] private static partial Regex UpdateWorkOrdner();
    [GeneratedRegex(@"^backup-[A-Za-z0-9._-]+$")] private static partial Regex UpdateRollbackOrdner();
    [GeneratedRegex(@"^linux-x64-[A-Za-z0-9._-]+\.(zip|tar\.gz)$")] private static partial Regex UpdatePaketDatei();
    [GeneratedRegex(@"^[A-Za-z0-9._-]+\.(zip|tar\.gz)$")] private static partial Regex DownloadDatei();
    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")] private static partial Regex ReleaseOrdner();

    private readonly SemaphoreSlim _lock = new(1, 1);

    public SpeicherPfade Pfade => pfade;

    public Task<SpeicherUebersicht> AuflistenAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Auflisten(DateTime.UtcNow), cancellationToken);

    public async Task<SpeicherLoeschErgebnis> LoeschenAsync(IEnumerable<(SpeicherBereich Bereich, string Name)> auswahl, CancellationToken cancellationToken = default)
    {
        var gewuenscht = auswahl.Distinct().ToList();
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Loeschen(gewuenscht, DateTime.UtcNow), cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    internal SpeicherUebersicht Auflisten(DateTime nowUtc)
    {
        var eintraege = new List<SpeicherEintrag>();
        var fehler = new Dictionary<SpeicherBereich, string>();

        Sammle(SpeicherBereich.ServerBackups, fehler, () => ServerBackups(nowUtc), eintraege);
        Sammle(SpeicherBereich.UpdatePakete, fehler, UpdatePakete, eintraege);
        Sammle(SpeicherBereich.UpdateDownloads, fehler, UpdateDownloads, eintraege);

        long? gesamt = null, frei = null;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(pfade.BackupVerzeichnis));
            if (!string.IsNullOrEmpty(root))
            {
                var laufwerk = new DriveInfo(root);
                gesamt = laufwerk.TotalSize;
                frei = laufwerk.AvailableFreeSpace;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Speicherplatz nicht lesbar");
        }

        return new SpeicherUebersicht(gesamt, frei, eintraege, fehler);
    }

    private void Sammle(SpeicherBereich bereich, Dictionary<SpeicherBereich, string> fehler, Func<IEnumerable<SpeicherEintrag>> quelle, List<SpeicherEintrag> ziel)
    {
        try
        {
            ziel.AddRange(quelle());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Speicherbereich {Bereich} nicht lesbar", bereich);
            fehler[bereich] = ex is UnauthorizedAccessException ? "Keine Leserechte." : ex.Message;
        }
    }

    private IEnumerable<SpeicherEintrag> ServerBackups(DateTime nowUtc)
    {
        var dir = new DirectoryInfo(pfade.BackupVerzeichnis);
        if (!dir.Exists) return [];

        var archive = dir.EnumerateFiles().Where(f => BackupDatei().IsMatch(f.Name))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal) // Zeitstempel im Namen
            .ToList();
        var neuestes = archive.FirstOrDefault()?.Name;

        var liste = archive.Select(f => new SpeicherEintrag(SpeicherBereich.ServerBackups, f.Name, f.Length, f.LastWriteTimeUtc,
            false, f.Name == neuestes, f.Name == neuestes ? "Neuestes Backup – geschützt" : null)).ToList();

        foreach (var tmp in dir.EnumerateDirectories().Where(d => BackupTmpOrdner().IsMatch(d.Name)))
        {
            var laeuft = nowUtc - tmp.LastWriteTimeUtc < LaufendesBackupFenster;
            liste.Add(new SpeicherEintrag(SpeicherBereich.ServerBackups, tmp.Name, OrdnerGroesse(tmp), tmp.LastWriteTimeUtc, true, laeuft,
                laeuft ? "Backup läuft evtl. noch – geschützt" : "Rest eines abgebrochenen Backups"));
        }

        return liste;
    }

    private IEnumerable<SpeicherEintrag> UpdatePakete()
    {
        var dir = new DirectoryInfo(pfade.UpdateVerzeichnis);
        if (!dir.Exists) return [];

        var liste = new List<SpeicherEintrag>();
        foreach (var d in dir.EnumerateDirectories().Where(d => UpdateWorkOrdner().IsMatch(d.Name)))
            liste.Add(new SpeicherEintrag(SpeicherBereich.UpdatePakete, d.Name, OrdnerGroesse(d), d.LastWriteTimeUtc, true, false, "Entpacktes Update"));

        var rollback = dir.EnumerateDirectories().Where(d => UpdateRollbackOrdner().IsMatch(d.Name))
            .OrderByDescending(d => d.LastWriteTimeUtc).ToList();
        for (var i = 0; i < rollback.Count; i++)
        {
            var d = rollback[i];
            liste.Add(new SpeicherEintrag(SpeicherBereich.UpdatePakete, d.Name, OrdnerGroesse(d), d.LastWriteTimeUtc, true, i == 0,
                i == 0 ? "Letzter Rollback-Stand – geschützt" : "Alter Rollback-Stand"));
        }

        foreach (var f in dir.EnumerateFiles().Where(f => UpdatePaketDatei().IsMatch(f.Name)))
            liste.Add(new SpeicherEintrag(SpeicherBereich.UpdatePakete, f.Name, f.Length, f.LastWriteTimeUtc, false, false, "Update-Paket"));

        return liste;
    }

    private IEnumerable<SpeicherEintrag> UpdateDownloads()
    {
        var dir = new DirectoryInfo(pfade.UpdateDownloadVerzeichnis);
        if (!dir.Exists) return [];

        var liste = new List<SpeicherEintrag>();
        var downloads = new DirectoryInfo(Path.Combine(dir.FullName, "downloads"));
        if (downloads.Exists)
        {
            foreach (var f in downloads.EnumerateFiles().Where(f => DownloadDatei().IsMatch(f.Name)))
                liste.Add(new SpeicherEintrag(SpeicherBereich.UpdateDownloads, "downloads/" + f.Name, f.Length, f.LastWriteTimeUtc, false, false, "Heruntergeladenes Paket"));
        }

        var releases = new DirectoryInfo(Path.Combine(dir.FullName, "releases"));
        if (releases.Exists)
        {
            foreach (var d in releases.EnumerateDirectories().Where(d => ReleaseOrdner().IsMatch(d.Name)))
                liste.Add(new SpeicherEintrag(SpeicherBereich.UpdateDownloads, "releases/" + d.Name, OrdnerGroesse(d), d.LastWriteTimeUtc, true, false, "Entpacktes Paket"));
        }

        return liste;
    }

    private SpeicherLoeschErgebnis Loeschen(IReadOnlyList<(SpeicherBereich Bereich, string Name)> auswahl, DateTime nowUtc)
    {
        // Frisch auflisten: nur was jetzt noch existiert, zum Bereich passt und nicht geschuetzt ist, wird geloescht
        var aktuell = Auflisten(nowUtc).Eintraege.ToDictionary(e => (e.Bereich, e.Name));
        var fehler = new List<string>();
        var geloescht = 0;
        long frei = 0;

        foreach (var schluessel in auswahl)
        {
            if (!aktuell.TryGetValue(schluessel, out var eintrag))
            {
                fehler.Add($"{schluessel.Name}: nicht (mehr) vorhanden.");
                continue;
            }

            if (eintrag.Geschuetzt)
            {
                fehler.Add($"{eintrag.Name}: geschützt, wird nicht gelöscht.");
                continue;
            }

            var pfad = PfadVon(eintrag);
            try
            {
                if (eintrag.IstOrdner) Directory.Delete(pfad, recursive: true);
                else File.Delete(pfad);

                geloescht++;
                frei += eintrag.Bytes;
                logger.LogInformation("Speicherverwaltung: {Bereich}/{Name} gelöscht ({Bytes} Bytes)", eintrag.Bereich, eintrag.Name, eintrag.Bytes);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Speicherverwaltung: keine Berechtigung für {Bereich}/{Name}", eintrag.Bereich, eintrag.Name);
                fehler.Add($"{eintrag.Name}: keine Berechtigung (Datei gehört einem anderen Benutzer, siehe DEPLOYMENT.md).");
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Speicherverwaltung: {Bereich}/{Name} nicht löschbar", eintrag.Bereich, eintrag.Name);
                fehler.Add($"{eintrag.Name}: {ex.Message}");
            }
        }

        return new SpeicherLoeschErgebnis(geloescht, frei, fehler);
    }

    private string PfadVon(SpeicherEintrag eintrag)
    {
        var basis = eintrag.Bereich switch
        {
            SpeicherBereich.ServerBackups => pfade.BackupVerzeichnis,
            SpeicherBereich.UpdatePakete => pfade.UpdateVerzeichnis,
            SpeicherBereich.UpdateDownloads => pfade.UpdateDownloadVerzeichnis,
            _ => throw new ArgumentOutOfRangeException(nameof(eintrag)),
        };

        var basisVoll = Path.GetFullPath(basis);
        var pfad = Path.GetFullPath(Path.Combine(basisVoll, eintrag.Name));
        if (!pfad.StartsWith(basisVoll + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Pfad liegt außerhalb des Bereichs.");
        return pfad;
    }

    private static long OrdnerGroesse(DirectoryInfo dir)
    {
        long summe = 0;
        try
        {
            var optionen = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var f in dir.EnumerateFiles("*", optionen)) summe += f.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Groesse unvollstaendig – Auflistung trotzdem anzeigen
        }

        return summe;
    }

    public static string FormatGroesse(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024 / 1024:0.0} GB",
        >= 1024L * 1024 => $"{bytes / 1024d / 1024:0} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B",
    };
}

using System.Diagnostics;
using Einsatzueberwachung.Domain.Interfaces;

namespace Einsatzueberwachung.Server.Services;

public enum ZertifikatErneuerungsErgebnis { Erneuert, NichtFaellig, Fehler }

public sealed record ZertifikatErneuerungsAntwort(ZertifikatErneuerungsErgebnis Ergebnis, string Text, string Ausgabe)
{
    public bool Erfolgreich => Ergebnis != ZertifikatErneuerungsErgebnis.Fehler;
}

/// <summary>
/// Knopf „Jetzt erneuern“ beim Zertifikatshinweis (1.11.6). Ruft das root-eigene Hilfsprogramm
/// /usr/local/sbin/einsatz-zertifikat per sudo auf (eingerichtet von deploy/scripts/setup-zertifikat.sh).
/// Domain und Webroot stehen root-eigen auf dem Server – aus der App werden keine Argumente durchgereicht.
/// </summary>
public sealed class ZertifikatErneuerung
{
    public const string ToolPath = "/usr/local/sbin/einsatz-zertifikat";
    private static readonly TimeSpan Zeitlimit = TimeSpan.FromMinutes(3);

    private readonly Func<CancellationToken, Task<(int Code, string Ausgabe)>> _ausfuehren;
    private readonly Func<bool> _verfuegbar;
    private readonly SystemZustandService? _systemZustand;
    private readonly IAuditLogService _audit;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ZertifikatErneuerung(SystemZustandService systemZustand, IAuditLogService audit)
        : this(SudoAusfuehrenAsync, () => OperatingSystem.IsLinux() && File.Exists(ToolPath), audit, systemZustand)
    {
    }

    internal ZertifikatErneuerung(Func<CancellationToken, Task<(int Code, string Ausgabe)>> ausfuehren, Func<bool> verfuegbar,
        IAuditLogService audit, SystemZustandService? systemZustand = null)
    {
        _ausfuehren = ausfuehren;
        _verfuegbar = verfuegbar;
        _audit = audit;
        _systemZustand = systemZustand;
    }

    /// <summary>Hilfsprogramm eingerichtet? Sonst zeigt die Oberflaeche den Hinweis auf setup-zertifikat.sh.</summary>
    public bool IsAvailable => _verfuegbar();

    public bool Laeuft => _lock.CurrentCount == 0;

    public event Action? Geaendert;

    public async Task<ZertifikatErneuerungsAntwort> ErneuernAsync(string? ausgeloestVon = null)
    {
        if (!IsAvailable)
            return new(ZertifikatErneuerungsErgebnis.Fehler,
                "Auf diesem Server nicht eingerichtet – einmalig „sudo bash deploy/scripts/setup-zertifikat.sh <domain>“ ausführen.", "");

        // Mehrere Leitungsplaetze koennen gleichzeitig druecken – certbot nur einmal starten
        if (!await _lock.WaitAsync(0))
            return new(ZertifikatErneuerungsErgebnis.Fehler, "Die Erneuerung läuft bereits.", "");

        Geaendert?.Invoke();
        ZertifikatErneuerungsAntwort antwort;
        try
        {
            using var cts = new CancellationTokenSource(Zeitlimit);
            var (code, ausgabe) = await _ausfuehren(cts.Token);
            antwort = Auswerten(code, ausgabe);
        }
        catch (OperationCanceledException)
        {
            antwort = new(ZertifikatErneuerungsErgebnis.Fehler, "Zeitüberschreitung – certbot hat nach 3 Minuten nicht geantwortet.", "");
        }
        catch (Exception ex)
        {
            antwort = new(ZertifikatErneuerungsErgebnis.Fehler, $"Hilfsprogramm nicht startbar: {ex.Message}", "");
        }
        finally
        {
            _lock.Release();
        }

        _audit.Log("Zertifikat erneuern", antwort.Text + (ausgeloestVon is null ? "" : $" (von {ausgeloestVon})"));
        if (_systemZustand is not null)
        {
            try { await _systemZustand.PruefenAsync(); } catch { /* Anzeige aktualisiert sich spaetestens beim naechsten Lauf */ }
        }
        Geaendert?.Invoke();
        return antwort;
    }

    internal static ZertifikatErneuerungsAntwort Auswerten(int code, string ausgabe)
    {
        var zeilen = ausgabe.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        var ergebnis = zeilen.FirstOrDefault(z => z.StartsWith("ergebnis=", StringComparison.Ordinal))?["ergebnis=".Length..];
        var rest = string.Join('\n', zeilen.Where(z => !z.StartsWith("ergebnis=", StringComparison.Ordinal)));

        return (code, ergebnis) switch
        {
            (0, "erneuert") => new(ZertifikatErneuerungsErgebnis.Erneuert, "Zertifikat erneuert, nginx neu geladen.", rest),
            (0, "nicht_faellig") => new(ZertifikatErneuerungsErgebnis.NichtFaellig,
                "Noch nicht fällig – Let's Encrypt erneuert erst 30 Tage vor Ablauf. Das Zertifikat bleibt gültig.", rest),
            _ => new(ZertifikatErneuerungsErgebnis.Fehler,
                "Erneuerung fehlgeschlagen. Häufigste Ursache: Port 80 ist von außen nicht erreichbar (Router-Weiterleitung) "
                + "oder die neue nginx-Vorlage fehlt.", rest)
        };
    }

    private static async Task<(int Code, string Ausgabe)> SudoAusfuehrenAsync(CancellationToken ct)
    {
        var info = new ProcessStartInfo("sudo") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-n");
        info.ArgumentList.Add(ToolPath);
        info.ArgumentList.Add("erneuern");

        using var process = Process.Start(info) ?? throw new InvalidOperationException("sudo nicht startbar.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* bereits beendet */ }
            throw;
        }
        return (process.ExitCode, (await stdout) + (await stderr));
    }
}

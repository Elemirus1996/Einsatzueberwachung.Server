using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Einsatzueberwachung.Server.Services;

public sealed record LoeschErgebnis(int Einsaetze, int Funksprueche);

/// <summary>
/// Loescht abgelaufene Daten nach den Fristen der Staffel (#151): Datenarten im Archiv, das gespeicherte PDF des Einsatzes
/// (es enthaelt die geloeschten Daten) und alte Funksprueche in der Funk-Datenbank. Laufende Einsaetze bleiben immer.
/// Ins Audit-Log kommt nur, was geloescht wurde, nie der Inhalt.
/// </summary>
public sealed class LoeschfristenService(
    IArchivService archiv,
    IEinsatzService einsatz,
    ISettingsService einstellungen,
    IAuditLogService? audit = null,
    IDbContextFactory<RuntimeDbContext>? funkDb = null,
    string? berichtOrdner = null)
{
    private readonly SemaphoreSlim _sperre = new(1, 1);

    /// <summary>Wie viele archivierte Einsaetze mit diesen Einstellungen sofort betroffen waeren (Rueckfrage vor dem Speichern)</summary>
    public async Task<int> BetroffenAsync(AppSettings neu, DateTime jetzt) =>
        (await archiv.GetAllArchivedAsync()).Count(a => Loeschfristen.Faellig(a, neu, jetzt).Count > 0);

    public async Task<LoeschErgebnis> AusfuehrenAsync(DateTime jetzt)
    {
        await _sperre.WaitAsync();
        try
        {
            var s = await einstellungen.GetAppSettingsAsync();
            var einsaetze = 0;
            foreach (var a in await archiv.GetAllArchivedAsync())
            {
                var faellig = Loeschfristen.Faellig(a, s, jetzt);
                if (faellig.Count == 0) continue;

                Loeschfristen.Anwenden(a, faellig, jetzt);
                await archiv.UpdateArchivedEinsatzAsync(a);
                BerichtLoeschen(a);
                einsaetze++;
                audit?.Log("Löschfrist abgelaufen",
                    $"{a.EinsatzTyp} {a.EinsatzNummer} vom {a.EinsatzDatum:dd.MM.yyyy}: {string.Join(", ", faellig.Select(Loeschfristen.Anzeige))} gelöscht");
            }

            var funk = await FunkLoeschenAsync(s, jetzt);
            if (funk > 0) audit?.Log("Löschfrist abgelaufen", $"{funk} Funksprüche aus der Funk-Datenbank gelöscht");
            return new LoeschErgebnis(einsaetze, funk);
        }
        finally
        {
            _sperre.Release();
        }
    }

    /// <summary>Gespeichertes PDF (Name wie beim Export); im Archiv kann es ohne die geloeschten Daten neu erzeugt werden</summary>
    private void BerichtLoeschen(ArchivedEinsatz a)
    {
        var ordner = berichtOrdner ?? AppPathResolver.GetReportDirectory();
        var datei = Path.Combine(ordner, $"Einsatzbericht_{a.EinsatzNummer}_{a.EinsatzDatum:yyyyMMdd}.pdf");
        try { if (File.Exists(datei)) File.Delete(datei); }
        catch (IOException) { /* naechster Lauf */ }
    }

    /// <summary>Funk-Datenbank nach der Frist fuer Notizen; nie Funk des laufenden Einsatzes</summary>
    private async Task<int> FunkLoeschenAsync(AppSettings s, DateTime jetzt)
    {
        if (funkDb is null || s.LoeschfristNotizenTage is not { } tage) return 0;
        var grenzeUtc = Ortszeit.NachUtc(jetzt.AddDays(-tage));
        var e = einsatz.CurrentEinsatz;
        if (ElPositionService.Laeuft(e))
        {
            var beginnUtc = Ortszeit.NachUtc(e.AlarmierungsZeit is { } alarm && alarm < e.EinsatzDatum ? alarm : e.EinsatzDatum);
            if (beginnUtc < grenzeUtc) grenzeUtc = beginnUtc;
        }

        await using var db = await funkDb.CreateDbContextAsync();
        var alt = await db.RadioMessages.Where(m => m.TimestampUtc < grenzeUtc).Select(m => m.Id).ToListAsync();
        if (alt.Count == 0) return 0;
        await db.RadioReplies.Where(r => alt.Contains(r.MessageId)).ExecuteDeleteAsync();
        return await db.RadioMessages.Where(m => alt.Contains(m.Id)).ExecuteDeleteAsync();
    }
}

/// <summary>Taeglicher Lauf der Loeschfristen, erster Lauf kurz nach dem Start</summary>
public sealed class LoeschfristenHintergrund(LoeschfristenService loeschen, ILogger<LoeschfristenHintergrund> logger) : BackgroundService
{
    public static readonly TimeSpan ErsterLauf = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Abstand = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(ErsterLauf, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var takt = new PeriodicTimer(Abstand);
        do
        {
            try
            {
                var ergebnis = await loeschen.AusfuehrenAsync(Ortszeit.Jetzt);
                if (ergebnis.Einsaetze > 0 || ergebnis.Funksprueche > 0)
                    logger.LogInformation("Löschfristen: {Einsaetze} Einsätze bereinigt, {Funk} Funksprüche gelöscht", ergebnis.Einsaetze, ergebnis.Funksprueche);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Löschfristen: Lauf fehlgeschlagen, nächster Versuch beim nächsten Takt");
            }
        }
        while (await takt.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}

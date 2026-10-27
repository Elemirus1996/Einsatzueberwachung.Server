using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Einsatzueberwachung.Server.Data;

/// <summary>
/// Speichert die Verlaeufe laufender Suchen (Halsband, Team-Handy) Punkt fuer Punkt in SQLite (#89, 1.11.7).
/// Frueher steckten sie gekuerzt auf 2000 Punkte im Gesamt-Schnappschuss – bei langen Suchen fehlte nach einem
/// Neustart der Anfang. Jetzt werden nur neue Punkte angehaengt; nachgesendete Punkte (Funkloch) koennen mitten im
/// Verlauf liegen, deshalb zaehlt Zeit+Ort als Kennung und beim Laden wird nach Zeit sortiert.
/// Wird ein Verlauf geleert oder ersetzt, werden seine Zeilen neu geschrieben. Nicht threadsicher – nur aus dem
/// Speicher-Takt von RuntimeStatePersistenceService aufrufen.
/// </summary>
public sealed class TrackPunktSpeicher<T>
{
    private readonly string _art;
    private readonly Func<T, DateTime> _zeit;
    private readonly Func<T, (double Lat, double Lon)> _ort;
    private readonly JsonSerializerOptions _json;
    private readonly Dictionary<string, HashSet<(long, double, double)>> _gespeichert = new();

    public TrackPunktSpeicher(string art, Func<T, DateTime> zeit, Func<T, (double Lat, double Lon)> ort, JsonSerializerOptions json)
    {
        _art = art;
        _zeit = zeit;
        _ort = ort;
        _json = json;
    }

    /// <summary>Schluessel, fuer die Zeilen in der Datenbank liegen (auch wenn die Quelle sie gerade nicht auflistet).</summary>
    public IReadOnlyCollection<string> GespeicherteSchluessel => _gespeichert.Keys;

    public static async Task TabelleAnlegenAsync(RuntimeDbContext db, CancellationToken ct)
    {
        // EnsureCreated legt in einer bestehenden Datenbank keine neuen Tabellen an
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "runtime_track_points" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_runtime_track_points" PRIMARY KEY AUTOINCREMENT,
                "Kind" TEXT NOT NULL,
                "Key" TEXT NOT NULL,
                "TimestampTicks" INTEGER NOT NULL,
                "Json" TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS "IX_runtime_track_points_Kind_Key" ON "runtime_track_points" ("Kind", "Key");
            """, ct);
    }

    public async Task<Dictionary<string, List<T>>> LadenAsync(RuntimeDbContext db, CancellationToken ct)
    {
        var zeilen = await db.TrackPoints.AsNoTracking()
            .Where(p => p.Kind == _art)
            .OrderBy(p => p.Key).ThenBy(p => p.TimestampTicks).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var ergebnis = new Dictionary<string, List<T>>();
        _gespeichert.Clear();
        foreach (var zeile in zeilen)
        {
            var punkt = JsonSerializer.Deserialize<T>(zeile.Json, _json);
            if (punkt is null) continue;
            if (!ergebnis.TryGetValue(zeile.Key, out var liste))
            {
                ergebnis[zeile.Key] = liste = new List<T>();
                _gespeichert[zeile.Key] = new HashSet<(long, double, double)>();
            }
            liste.Add(punkt);
            _gespeichert[zeile.Key].Add(Kennung(punkt));
        }
        return ergebnis;
    }

    /// <summary>Gleicht die Datenbank mit den aktuellen Verlaeufen ab. Ein leerer oder fehlender Verlauf loescht seine Zeilen.</summary>
    public async Task AbgleichenAsync(RuntimeDbContext db, IReadOnlyDictionary<string, IReadOnlyList<T>> verlaeufe, CancellationToken ct)
    {
        foreach (var key in _gespeichert.Keys.Where(k => !verlaeufe.TryGetValue(k, out var v) || v.Count == 0).ToList())
        {
            await db.TrackPoints.Where(p => p.Kind == _art && p.Key == key).ExecuteDeleteAsync(ct);
            _gespeichert.Remove(key);
        }

        foreach (var (key, punkte) in verlaeufe)
        {
            if (punkte.Count == 0) continue;
            var aktuell = punkte.Select(Kennung).ToHashSet();

            if (_gespeichert.TryGetValue(key, out var gespeichert) && !gespeichert.IsSubsetOf(aktuell))
            {
                // Verlauf wurde ersetzt (neue Suche, Wiederherstellung) – komplett neu schreiben
                await db.TrackPoints.Where(p => p.Kind == _art && p.Key == key).ExecuteDeleteAsync(ct);
                gespeichert = null;
            }
            gespeichert ??= _gespeichert[key] = new HashSet<(long, double, double)>();

            foreach (var punkt in punkte)
            {
                if (!gespeichert.Add(Kennung(punkt))) continue;
                db.TrackPoints.Add(new TrackPointEntity
                {
                    Kind = _art,
                    Key = key,
                    TimestampTicks = _zeit(punkt).Ticks,
                    Json = JsonSerializer.Serialize(punkt, _json)
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private (long, double, double) Kennung(T punkt)
    {
        var (lat, lon) = _ort(punkt);
        return (_zeit(punkt).Ticks, lat, lon);
    }
}

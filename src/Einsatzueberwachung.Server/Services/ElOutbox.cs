using System.Collections.Concurrent;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Im Funkloch auf dem Geraet erfasster Funkspruch oder EL-Notiz (js/el-outbox.js).</summary>
public sealed record ElOutboxEntry(string Id, string Kind, string Text, string? Recipient, DateTimeOffset CreatedAt);

/// <summary>
/// Nachgesendete Eintraege der EL-Ansicht: prueft sie, merkt sich bereits verarbeitete IDs (ein Geraet sendet nach
/// einem Abbruch denselben Eintrag evtl. zweimal) und vermerkt die Erfassungszeit im Text, weil der Eintrag erst
/// mit der Zustellzeit im Funkstammbuch landet.
/// </summary>
public sealed class ElOutboxService
{
    public const int MaxTextLength = 2000;
    public const int MaxRecipientLength = 100;
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _processed = new(StringComparer.Ordinal);

    public static string? Validate(ElOutboxEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 64) return "Ungueltige ID.";
        if (entry.Kind is not ("funk" or "notiz")) return "Unbekannte Art.";
        if (string.IsNullOrWhiteSpace(entry.Text)) return "Text darf nicht leer sein.";
        if (entry.Text.Length > MaxTextLength) return "Text ist zu lang.";
        if (entry.Recipient is { Length: > MaxRecipientLength }) return "Empfaenger ist zu lang.";
        return null;
    }

    /// <summary>true, wenn die ID neu ist (und jetzt als verarbeitet gilt); false bei einem doppelt gesendeten Eintrag.</summary>
    public bool TryClaim(string id, DateTimeOffset now)
    {
        foreach (var old in _processed.Where(p => now - p.Value > MaxAge).Select(p => p.Key).ToList())
        {
            _processed.TryRemove(old, out _);
        }

        return _processed.TryAdd(id, now);
    }

    /// <summary>Verarbeitung fehlgeschlagen: ID wieder freigeben, damit das Geraet erneut senden kann.</summary>
    public void Release(string id) => _processed.TryRemove(id, out _);

    /// <summary>
    /// Text fuer Funkstammbuch/Notizen: Empfaenger voran, Erfassungszeit dahinter, wenn spaeter zugestellt.
    /// <paramref name="localNow"/> ist jetzt in der Zeitzone der Staffel (ITimeService.Now) – fuer die Anzeige.
    /// </summary>
    public static string BuildText(ElOutboxEntry entry, DateTimeOffset now, DateTime localNow)
    {
        var text = entry.Text.Trim();
        if (entry.Kind == "funk" && !string.IsNullOrWhiteSpace(entry.Recipient))
        {
            text = $"@{entry.Recipient.Trim()} {text}";
        }

        // Uhr des Geraets kann falsch gehen: nie in der Zukunft, nie aelter als 24 h
        var created = entry.CreatedAt > now ? now : entry.CreatedAt < now - MaxAge ? now - MaxAge : entry.CreatedAt;
        var age = now - created;
        if (age >= TimeSpan.FromMinutes(1))
        {
            text += $" [im Funkloch erfasst {localNow - age:HH:mm}, nachgesendet]";
        }

        return text;
    }
}

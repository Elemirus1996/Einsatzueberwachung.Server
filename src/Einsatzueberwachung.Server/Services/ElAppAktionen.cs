using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Aktionen der nativen Einsatzleitung (#125, docs/APP_API.md Abschnitt 7): Jede id wird nur einmal ausgefuehrt, damit die
/// App im Funkloch puffern und wiederholen darf. Gemerkt werden nur erfolgreiche Aktionen; abgelehnte darf die App mit
/// derselben id erneut senden.
/// </summary>
public sealed class ElAppAktionen
{
    private readonly ConcurrentDictionary<string, DateTime> _ausgefuehrt = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sperre = new(1, 1);

    public async Task<IActionResult> AusfuehrenAsync(string id, Func<Task<IActionResult>> aktion, DateTime nowUtc)
    {
        // Nacheinander: zwei Geraete der Einsatzleitung duerfen nicht gleichzeitig dasselbe Team umschalten
        await _sperre.WaitAsync();
        try
        {
            foreach (var alt in _ausgefuehrt.Where(p => p.Value < nowUtc.AddHours(-24)).Select(p => p.Key).ToList())
                _ausgefuehrt.TryRemove(alt, out _);
            if (_ausgefuehrt.ContainsKey(id)) return new OkObjectResult(new { doppelt = true });

            var ergebnis = await aktion();
            if (ergebnis is OkObjectResult) _ausgefuehrt[id] = nowUtc;
            return ergebnis;
        }
        finally
        {
            _sperre.Release();
        }
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Security;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Token der Tracking-App fuer die Rolle Einsatzleitung (#110, docs/APP_API.md). Ausgestellt nur von der angemeldeten
/// EL-Ansicht (/el) im VPN; gebunden an die Generation der EL-Anmeldung – eine Passwortaenderung entwertet es sofort.
/// Format: „e1.&lt;generation&gt;.&lt;ablauf-unix&gt;.&lt;signatur&gt;“.
/// </summary>
public sealed class ElAppTokenService(byte[] hmacSecret, LeitungAuthStore leitung)
{
    private const string Version = "e1";
    public static readonly TimeSpan Laufzeit = TimeSpan.FromHours(24);

    public string Ausstellen(DateTime ablaufUtc)
    {
        var ablauf = new DateTimeOffset(DateTime.SpecifyKind(ablaufUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var payload = $"{Version}.{leitung.Generation}.{ablauf}";
        return $"{payload}.{Signatur(payload)}";
    }

    public bool TryPruefen(string? token, DateTime nowUtc, out DateTime ablaufUtc)
    {
        ablaufUtc = default;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return false;
        var teile = token.Split('.');
        if (teile.Length != 4 || teile[0] != Version) return false;
        if (!int.TryParse(teile[1], out var generation) || !long.TryParse(teile[2], out var ablauf)) return false;
        if (generation != leitung.Generation) return false;

        var erwartet = Signatur($"{teile[0]}.{teile[1]}.{teile[2]}");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(teile[3]), Encoding.ASCII.GetBytes(erwartet)))
            return false;

        ablaufUtc = DateTimeOffset.FromUnixTimeSeconds(ablauf).UtcDateTime;
        return ablaufUtc > nowUtc;
    }

    private string Signatur(string payload)
    {
        using var hmac = new HMACSHA256(hmacSecret);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes("el|" + payload)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

/// <summary>Letzte Position der Einsatzleitung je Geraet – fuer den Fuehrungsassistenten auf der Einsatzkarte.</summary>
public sealed record ElPosition(string Geraet, double Latitude, double Longitude, double? Accuracy, DateTime Zeit, int? BatteryPercent);

/// <summary>
/// Positionen der Einsatzleitung (nur fluechtig, nicht im Bericht). Angenommen wird nur waehrend eines Einsatzes oder
/// einer Uebung; danach antwortet die Schnittstelle „pause“ und die App schaltet GPS ab.
/// </summary>
public sealed class ElPositionService
{
    private readonly IEinsatzService _einsatz;
    private readonly ConcurrentDictionary<string, ElPosition> _positionen = new(StringComparer.Ordinal);

    public ElPositionService(IEinsatzService einsatz)
    {
        _einsatz = einsatz;
        // Einsatz beendet oder neuer Einsatz: alte EL-Positionen nicht weiter zeigen
        _einsatz.EinsatzChanged += () => { if (!Laeuft(_einsatz.CurrentEinsatz)) Leeren(); };
    }

    public event Action<ElPosition>? Geaendert;
    public event Action? Geleert;

    public static bool Laeuft(EinsatzData? einsatz) =>
        einsatz is { EinsatzEnde: null } && !string.IsNullOrWhiteSpace(einsatz.Einsatzort);

    public OrtungsModus Modus => Laeuft(_einsatz.CurrentEinsatz) ? OrtungsModus.Track : OrtungsModus.Pause;

    public IReadOnlyCollection<ElPosition> Aktuell => Laeuft(_einsatz.CurrentEinsatz) ? _positionen.Values.ToList() : [];

    /// <summary>Nimmt Positionen an (Puffer, beliebige Reihenfolge); behaelt die neueste je Geraet.</summary>
    public int Melden(string geraet, IEnumerable<(double Lat, double Lng, double? Accuracy, DateTime ZeitUtc, int? Akku)> punkte)
    {
        if (!Laeuft(_einsatz.CurrentEinsatz)) return 0;

        var neueste = punkte
            .Where(p => double.IsFinite(p.Lat) && double.IsFinite(p.Lng) && Math.Abs(p.Lat) <= 90 && Math.Abs(p.Lng) <= 180)
            .OrderBy(p => p.ZeitUtc)
            .ToList();
        if (neueste.Count == 0) return 0;

        var letzte = neueste[^1];
        var position = new ElPosition(geraet, letzte.Lat, letzte.Lng, letzte.Accuracy, Ortszeit.AusUtc(letzte.ZeitUtc),
            letzte.Akku is >= 0 and <= 100 ? letzte.Akku : null);
        var uebernommen = _positionen.AddOrUpdate(geraet, position, (_, alt) => alt.Zeit > position.Zeit ? alt : position);
        if (ReferenceEquals(uebernommen, position)) Geaendert?.Invoke(position);
        return neueste.Count;
    }

    private void Leeren()
    {
        if (_positionen.IsEmpty) return;
        _positionen.Clear();
        Geleert?.Invoke();
    }
}

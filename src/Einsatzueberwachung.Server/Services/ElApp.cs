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
/// Format: „e3.&lt;generation&gt;.&lt;ablauf-unix&gt;.&lt;anmeldung-unix&gt;.&lt;kennung&gt;.&lt;signatur&gt;“ mit zufaelliger Kennung
/// (16 Byte) – zwei Anmeldungen in derselben Sekunde ergeben verschiedene Tokens, ein Logout sperrt nur eins. Der Zeitpunkt der
/// Anmeldung mit Passwort begrenzt das Verlaengern (#141). Aeltere „e2“- und „e1“-Tokens werden bis zu ihrem Ablauf weiter
/// angenommen; als Anmeldung gilt bei ihnen Ablauf − Laufzeit.
/// </summary>
public sealed class ElAppTokenService(byte[] hmacSecret, LeitungAuthStore leitung, string? sperrDatei = null)
{
    private const string Version = "e3";
    private const string VersionMitKennung = "e2";
    private const string VersionAlt = "e1";
    public static readonly TimeSpan Laufzeit = TimeSpan.FromHours(24);
    /// <summary>Hoechstens so lange nach der Anmeldung mit Passwort darf verlaengert werden (#141)</summary>
    public static readonly TimeSpan HoechstensVerlaengertBis = TimeSpan.FromHours(72);

    // Abgemeldete Tokens (POST /logout): Hash → Ablauf. Dauerhaft in einer Datei, damit ein Neustart sie nicht wieder freigibt.
    private readonly object _sperre = new();
    private Dictionary<string, DateTime>? _abgemeldet;

    public static ElAppTokenService MitDatenordner(byte[] hmacSecret, LeitungAuthStore leitung) =>
        new(hmacSecret, leitung, Path.Combine(AppPathResolver.GetDataDirectory(), "el-app-abgemeldet.txt"));

    /// <summary>Token sofort und dauerhaft ungueltig machen (bis zu seinem Ablauf gemerkt)</summary>
    public void Abmelden(string token, DateTime ablaufUtc)
    {
        lock (_sperre)
        {
            var liste = Abgemeldet();
            liste[Hash(token)] = ablaufUtc;
            foreach (var alt in liste.Where(p => p.Value <= DateTime.UtcNow).Select(p => p.Key).ToList()) liste.Remove(alt);
            if (sperrDatei is not null)
                File.WriteAllLines(sperrDatei, liste.Select(p => $"{p.Key}|{new DateTimeOffset(p.Value).ToUnixTimeSeconds()}"));
        }
    }

    private bool IstAbgemeldet(string token)
    {
        lock (_sperre) return Abgemeldet().ContainsKey(Hash(token));
    }

    private Dictionary<string, DateTime> Abgemeldet()
    {
        if (_abgemeldet is not null) return _abgemeldet;
        _abgemeldet = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        if (sperrDatei is not null && File.Exists(sperrDatei))
            foreach (var zeile in File.ReadAllLines(sperrDatei))
            {
                var teile = zeile.Split('|');
                if (teile.Length == 2 && long.TryParse(teile[1], out var ab))
                    _abgemeldet[teile[0]] = DateTimeOffset.FromUnixTimeSeconds(ab).UtcDateTime;
            }
        return _abgemeldet;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <param name="ablaufUtc">Ablauf des Tokens</param>
    /// <param name="anmeldungUtc">Anmeldung mit Passwort; ohne Angabe jetzt (beim Verlaengern die des alten Tokens)</param>
    public string Ausstellen(DateTime ablaufUtc, DateTime? anmeldungUtc = null)
    {
        static long Unix(DateTime zeit) => new DateTimeOffset(DateTime.SpecifyKind(zeit, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var kennung = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var payload = $"{Version}.{leitung.Generation}.{Unix(ablaufUtc)}.{Unix(anmeldungUtc ?? DateTime.UtcNow)}.{kennung}";
        return $"{payload}.{Signatur(payload)}";
    }

    public bool TryPruefen(string? token, DateTime nowUtc, out DateTime ablaufUtc) => TryPruefen(token, nowUtc, out ablaufUtc, out _);

    public bool TryPruefen(string? token, DateTime nowUtc, out DateTime ablaufUtc, out DateTime anmeldungUtc)
    {
        ablaufUtc = default;
        anmeldungUtc = default;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return false;
        var teile = token.Split('.');
        var e3 = teile.Length == 6 && teile[0] == Version;
        var e2 = teile.Length == 5 && teile[0] == VersionMitKennung;
        var e1 = teile.Length == 4 && teile[0] == VersionAlt;
        if (!e3 && !e2 && !e1) return false;
        if (!int.TryParse(teile[1], out var generation) || !long.TryParse(teile[2], out var ablauf)) return false;
        long anmeldung = 0;
        if (e3 && !long.TryParse(teile[3], out anmeldung)) return false;
        if (generation != leitung.Generation) return false;

        var payload = string.Join('.', teile[..^1]);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(teile[^1]), Encoding.ASCII.GetBytes(Signatur(payload))))
            return false;

        ablaufUtc = DateTimeOffset.FromUnixTimeSeconds(ablauf).UtcDateTime;
        anmeldungUtc = e3 ? DateTimeOffset.FromUnixTimeSeconds(anmeldung).UtcDateTime : ablaufUtc - Laufzeit;
        return ablaufUtc > nowUtc && !IstAbgemeldet(token);
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

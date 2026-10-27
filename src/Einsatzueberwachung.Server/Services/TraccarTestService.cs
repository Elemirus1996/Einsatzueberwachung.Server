using System.Security.Cryptography;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Zuletzt empfangene Testposition (Einstellungen → Integration → Traccar testen)</summary>
public sealed record TraccarTestEmpfang(DateTime EmpfangenUtc, double Latitude, double Longitude, double? Accuracy, int? BatteryPercent, string Format);

/// <summary>
/// Traccar-Test ohne Einsatz: Die Einstellungen erzeugen eine Test-Kennung (gilt 1 Stunde), die Traccar-App wird
/// damit eingerichtet, und der Traccar-Endpunkt meldet empfangene Testpositionen hierher statt in einen Einsatz.
/// So laesst sich der ganze Weg pruefen: Handy → Zertifikat → nginx → App.
/// </summary>
public sealed class TraccarTestService
{
    public const string Praefix = "test-";
    private static readonly TimeSpan Gueltigkeit = TimeSpan.FromHours(1);

    private readonly object _lock = new();
    private string? _kennung;
    private DateTime _gueltigBisUtc;

    public TraccarTestEmpfang? LetzterEmpfang { get; private set; }

    public event Action? Geaendert;

    public string NeueKennung(DateTime nowUtc)
    {
        lock (_lock)
        {
            _kennung = Praefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            _gueltigBisUtc = nowUtc + Gueltigkeit;
            LetzterEmpfang = null;
            return _kennung;
        }
    }

    public string? AktuelleKennung(DateTime nowUtc)
    {
        lock (_lock) return _kennung is not null && nowUtc < _gueltigBisUtc ? _kennung : null;
    }

    /// <summary>true, wenn <paramref name="deviceId"/> die gueltige Test-Kennung ist (dann als Test vermerkt)</summary>
    public bool TryVermerke(string deviceId, double lat, double lon, double? accuracy, int? battery, string format, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (_kennung is null || nowUtc >= _gueltigBisUtc
                || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(deviceId), System.Text.Encoding.UTF8.GetBytes(_kennung)))
            {
                return false;
            }

            LetzterEmpfang = new TraccarTestEmpfang(nowUtc, lat, lon, accuracy, battery, format);
        }

        Geaendert?.Invoke();
        return true;
    }

    /// <summary>Einrichtungs-Link fuer die Traccar-App (wie auf der Team-Seite)</summary>
    public static string EinrichtungsLink(string endpoint, string kennung) =>
        "org.traccar.client://?url=" + Uri.EscapeDataString(endpoint)
        + "&id=" + Uri.EscapeDataString(kennung)
        + "&accuracy=highest&interval=10&distance=5&angle=30&buffer=true&wakelock=true&stop_detection=false";
}

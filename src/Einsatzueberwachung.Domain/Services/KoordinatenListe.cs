using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Einsatzueberwachung.Domain.Services
{
    public enum KoordinatenFormat
    {
        Automatisch,
        /// <summary>Dezimalgrad „Breite,Länge“, z. B. 51.70913193,10.53388506</summary>
        BreiteLaenge,
        /// <summary>Dezimalgrad „Länge,Breite“ (Reihenfolge wie GeoJSON/GPX-Tools)</summary>
        LaengeBreite,
        /// <summary>UTM, z. B. 32U 612345 5732123</summary>
        Utm
    }

    /// <summary>
    /// Suchgebiet aus einer eingefuegten Koordinatenliste anderer Apps und zurueck (#96, 1.11.7).
    /// </summary>
    public static class KoordinatenListe
    {
        public static IReadOnlyList<(KoordinatenFormat Format, string Name)> Formate { get; } =
        [
            (KoordinatenFormat.Automatisch, "Automatisch erkennen"),
            (KoordinatenFormat.BreiteLaenge, "Dezimal: Breite, Länge"),
            (KoordinatenFormat.LaengeBreite, "Dezimal: Länge, Breite"),
            (KoordinatenFormat.Utm, "UTM")
        ];

        public static string Name(KoordinatenFormat format) => Formate.First(f => f.Format == format).Name;

        private static readonly Regex UtmPunkt = new(
            @"(?<zone>\d{1,2})\s*(?<band>[C-HJ-NP-Xc-hj-np-x])\s*[,;]?\s*(?<ost>\d{5,7}(?:\.\d+)?)\s*[Ee]?\s*[,;]?\s*(?<nord>\d{6,8}(?:\.\d+)?)\s*[Nn]?",
            RegexOptions.Compiled);

        private static readonly Regex Zahl = new(@"[-+]?\d+(?:\.\d+)?", RegexOptions.Compiled);

        public sealed record Ergebnis(List<(double Lat, double Lon)> Punkte, KoordinatenFormat ErkanntesFormat);

        /// <summary>Liest die Liste ein. Liefert null und eine Fehlermeldung, wenn kein gueltiges Gebiet entsteht.</summary>
        public static Ergebnis? Einlesen(string? text, KoordinatenFormat format, out string? fehler)
        {
            fehler = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                fehler = "Bitte eine Koordinatenliste einfügen.";
                return null;
            }

            if (format == KoordinatenFormat.Utm || (format == KoordinatenFormat.Automatisch && UtmPunkt.IsMatch(text)))
            {
                var punkte = new List<(double, double)>();
                foreach (Match m in UtmPunkt.Matches(text))
                {
                    var (lat, lon) = UtmConverter.UtmToLatLong(
                        int.Parse(m.Groups["zone"].Value, CultureInfo.InvariantCulture),
                        char.ToUpperInvariant(m.Groups["band"].Value[0]),
                        double.Parse(m.Groups["ost"].Value, CultureInfo.InvariantCulture),
                        double.Parse(m.Groups["nord"].Value, CultureInfo.InvariantCulture));
                    punkte.Add((lat, lon));
                }
                return Pruefen(punkte, KoordinatenFormat.Utm, out fehler);
            }

            var zahlen = Zahl.Matches(text).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
            if (zahlen.Count % 2 != 0)
            {
                fehler = $"Ungerade Anzahl an Zahlen ({zahlen.Count}) – zu jedem Punkt gehören zwei Werte. Dezimaltrennzeichen ist der Punkt.";
                return null;
            }

            var paare = Enumerable.Range(0, zahlen.Count / 2).Select(i => (A: zahlen[2 * i], B: zahlen[2 * i + 1])).ToList();
            var reihenfolge = format == KoordinatenFormat.Automatisch ? ReihenfolgeErkennen(paare) : format;
            var liste = reihenfolge == KoordinatenFormat.LaengeBreite
                ? paare.Select(p => (p.B, p.A)).ToList()
                : paare.Select(p => (p.A, p.B)).ToList();
            return Pruefen(liste, reihenfolge, out fehler);
        }

        private static KoordinatenFormat ReihenfolgeErkennen(List<(double A, double B)> paare)
        {
            // Eindeutig, wenn ein Wert nur als Laenge gueltig ist (|x| > 90)
            if (paare.Any(p => Math.Abs(p.A) > 90)) return KoordinatenFormat.LaengeBreite;
            if (paare.Any(p => Math.Abs(p.B) > 90)) return KoordinatenFormat.BreiteLaenge;
            // In Mitteleuropa ist die Breite (45–56) groesser als die Laenge (5–17)
            return paare.Count > 0 && paare.Average(p => p.A) < paare.Average(p => p.B)
                ? KoordinatenFormat.LaengeBreite
                : KoordinatenFormat.BreiteLaenge;
        }

        private static Ergebnis? Pruefen(List<(double Lat, double Lon)> punkte, KoordinatenFormat format, out string? fehler)
        {
            fehler = null;
            // Geschlossener Ring: letzter Punkt = erster Punkt wird nicht doppelt gespeichert
            if (punkte.Count > 1 && Nah(punkte[0], punkte[^1])) punkte.RemoveAt(punkte.Count - 1);

            if (punkte.Count < 3)
            {
                fehler = $"Für ein Suchgebiet werden mindestens 3 Punkte gebraucht (erkannt: {punkte.Count}).";
                return null;
            }
            if (punkte.FirstOrDefault(p => Math.Abs(p.Lat) > 90 || Math.Abs(p.Lon) > 180) is { } falsch && falsch != default)
            {
                fehler = $"Ungültige Koordinate {falsch.Lat.ToString("0.######", CultureInfo.InvariantCulture)}, {falsch.Lon.ToString("0.######", CultureInfo.InvariantCulture)}.";
                return null;
            }
            return new Ergebnis(punkte, format);
        }

        private static bool Nah((double Lat, double Lon) a, (double Lat, double Lon) b) =>
            Math.Abs(a.Lat - b.Lat) < 1e-9 && Math.Abs(a.Lon - b.Lon) < 1e-9;

        /// <summary>Gibt ein Suchgebiet als Liste aus – wieder einlesbar, auch von anderen Apps.</summary>
        public static string Ausgeben(IEnumerable<(double Lat, double Lon)> punkte, KoordinatenFormat format)
        {
            var inv = CultureInfo.InvariantCulture;
            var liste = punkte.ToList();
            return format switch
            {
                KoordinatenFormat.LaengeBreite => string.Join(" ", liste.Select(p => $"{p.Lon.ToString("0.00000000", inv)},{p.Lat.ToString("0.00000000", inv)}")),
                KoordinatenFormat.Utm => string.Join("\n", liste.Select(p =>
                {
                    var (zone, band, ost, nord) = UtmConverter.LatLongToUtm(p.Lat, p.Lon);
                    return $"{zone}{band} {Math.Round(ost).ToString("0", inv)} {Math.Round(nord).ToString("0", inv)}";
                })),
                _ => string.Join(" ", liste.Select(p => $"{p.Lat.ToString("0.00000000", inv)},{p.Lon.ToString("0.00000000", inv)}"))
            };
        }

        /// <summary>GeoJSON wie aus der Zeichnung auf der Karte (Feature/Polygon, [Länge, Breite], geschlossener Ring).</summary>
        public static string AlsGeoJson(IReadOnlyList<(double Lat, double Lon)> punkte)
        {
            var ring = punkte.Select(p => new[] { p.Lon, p.Lat }).ToList();
            ring.Add(new[] { punkte[0].Lon, punkte[0].Lat });
            return JsonSerializer.Serialize(new
            {
                type = "Feature",
                properties = new { },
                geometry = new { type = "Polygon", coordinates = new[] { ring } }
            });
        }
    }
}

using System.Text.RegularExpressions;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

[CollectionDefinition(nameof(OrtszeitTests), DisableParallelization = true)]
public sealed class OrtszeitSammlung;

/// <summary>Einheitliche Zeitzone statt Serveruhr (#87, 1.11.7). Setzt die globale Zone – daher nicht parallel.</summary>
[Collection(nameof(OrtszeitTests))]
public class OrtszeitTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static void MitBerlin(Action pruefung)
    {
        var vorher = Ortszeit.Zone;
        Ortszeit.Zone = Berlin;
        try { pruefung(); }
        finally { Ortszeit.Zone = vorher; }
    }

    [Fact]
    public void JetztUndUmrechnungen_NutzenDieEingestellteZone() => MitBerlin(() =>
    {
        var erwartet = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Berlin);
        Assert.InRange(Ortszeit.Jetzt, erwartet.AddSeconds(-5), erwartet.AddSeconds(5));

        var sommerUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 7, 1, 12, 0, 0), Ortszeit.AusUtc(sommerUtc));
        Assert.Equal(sommerUtc, Ortszeit.NachUtc(new DateTime(2026, 7, 1, 12, 0, 0)));

        // Unbestimmt = bereits Ortszeit (ITimeService) – unveraendert anzeigen
        var ortszeit = new DateTime(2026, 1, 5, 9, 30, 0);
        Assert.Equal(ortszeit, Ortszeit.AusUtc(ortszeit));
        Assert.Null(Ortszeit.AusUtc((DateTime?)null));
    });

    [Fact]
    public void ZeitInDerLueckeDerSommerzeitumstellung_WirftNicht() => MitBerlin(() =>
    {
        var luecke = new DateTime(2026, 3, 29, 2, 30, 0); // gibt es in Berlin nicht
        Assert.Equal(DateTimeKind.Utc, Ortszeit.NachUtc(luecke).Kind);
    });

    [Fact]
    public void KeinNeuesDateTimeNowImCode()
    {
        var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var verboten = new Regex(@"\bDateTime\.Now\b|\.ToLocalTime\(\)");
        var dateien = new[] { "Einsatzueberwachung.Domain", "Einsatzueberwachung.Server" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(src, p), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".razor"))
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && Path.GetFileName(f) != "Ortszeit.cs")
            .ToList();
        Assert.True(dateien.Count > 100, $"Quellcode nicht gefunden unter {src}");

        var treffer = dateien
            .SelectMany(f => File.ReadLines(f).Select((zeile, i) => (f, i, zeile)))
            .Where(x => verboten.IsMatch(x.zeile) && !x.zeile.TrimStart().StartsWith("//"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.zeile.Trim()}")
            .ToList();

        Assert.True(treffer.Count == 0,
            "Serveruhr statt eingestellter Zeitzone – Ortszeit.Jetzt / ITimeService.Now bzw. Ortszeit.AusUtc verwenden:\n"
            + string.Join("\n", treffer));
    }
}

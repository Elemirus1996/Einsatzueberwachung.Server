using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

/// <summary>Abdeckung eines Suchgebiets fuer die Karte: Prozent und abgesuchte Rasterzellen (Mittelpunkte)</summary>
public sealed record GebietsAbdeckung(string AreaId, double Prozent, double ZellGroesseMeter, IReadOnlyList<double[]> AbgesuchteZellen);

/// <summary>
/// Abdeckung der Suchgebiete live auf der Einsatzkarte – dieselbe Rechnung wie im Bericht
/// (<see cref="CoverageAnalysisCalculator"/>): abgeschlossene Suchen im Gebiet plus laufende Halsband- und
/// Handy-Tracks der Teams, die dem Gebiet zugeteilt sind. Ergebnis wird kurz zwischengespeichert, damit mehrere
/// offene Karten nicht mehrfach rechnen.
/// </summary>
public sealed class LiveAbdeckungService(IEinsatzService einsatzService, ICollarTrackingService collarTracking)
{
    private static readonly TimeSpan Zwischenspeicher = TimeSpan.FromSeconds(15);
    private readonly object _lock = new();
    private IReadOnlyList<GebietsAbdeckung> _letzte = [];
    private DateTime _berechnetUtc = DateTime.MinValue;

    public IReadOnlyList<GebietsAbdeckung> Aktuell()
    {
        lock (_lock)
        {
            if (DateTime.UtcNow - _berechnetUtc < Zwischenspeicher) return _letzte;
        }

        var ergebnis = Berechne(einsatzService.CurrentEinsatz, einsatzService.Teams,
            id => collarTracking.GetLocationHistory(id).Select(l => new TrackPoint { Latitude = l.Latitude, Longitude = l.Longitude }),
            id => einsatzService.GetPhoneTrackHistory(id).Select(l => new TrackPoint { Latitude = l.Latitude, Longitude = l.Longitude }));

        lock (_lock)
        {
            _letzte = ergebnis;
            _berechnetUtc = DateTime.UtcNow;
        }
        return ergebnis;
    }

    public static IReadOnlyList<GebietsAbdeckung> Berechne(
        EinsatzData einsatz,
        IEnumerable<Team> teams,
        Func<string, IEnumerable<TrackPoint>> halsbandVerlauf,
        Func<string, IEnumerable<TrackPoint>> handyVerlauf)
    {
        var ergebnis = new List<GebietsAbdeckung>();
        var teamListe = teams.ToList();

        foreach (var gebiet in einsatz.SearchAreas ?? [])
        {
            if (gebiet.Coordinates is not { Count: >= 3 }) continue;

            var punkte = PunkteFuerGebiet(gebiet, einsatz, teamListe, halsbandVerlauf, handyVerlauf);
            if (punkte.Count == 0) continue;

            var abdeckung = CoverageAnalysisCalculator.CalculateForArea(gebiet.Coordinates, gebiet.Name, gebiet.Color,
                punkte, coverageRadiusMeters: 25, cellSizeMeters: 15, maxCellsPerArea: 4000);
            if (abdeckung is null) continue;

            ergebnis.Add(new GebietsAbdeckung(
                gebiet.Id,
                Math.Round(abdeckung.CoveragePercent, 1),
                abdeckung.CellSizeMeters,
                abdeckung.Cells.Where(c => c.Covered).Select(c => new[] { c.Latitude, c.Longitude }).ToList()));
        }

        return ergebnis;
    }

    /// <summary>Abgeschlossene Suchen im Gebiet (ohne doppelte Tracks) und laufende Tracks der zugeteilten Teams</summary>
    public static List<TrackPoint> PunkteFuerGebiet(
        SearchArea gebiet,
        EinsatzData einsatz,
        IReadOnlyList<Team> teams,
        Func<string, IEnumerable<TrackPoint>> halsbandVerlauf,
        Func<string, IEnumerable<TrackPoint>> handyVerlauf)
    {
        var tracks = new Dictionary<string, TeamTrackSnapshot>(StringComparer.Ordinal);
        foreach (var suche in einsatz.CompletedSearches ?? [])
        {
            if (suche.SearchAreaId != gebiet.Id) continue;
            foreach (var track in suche.Tracks) tracks[track.Id] = track;
        }

        // Aeltere Tracks ohne abgeschlossene Suche: ueber den Gebietsnamen
        foreach (var track in einsatz.TrackSnapshots ?? [])
        {
            if (!string.IsNullOrEmpty(gebiet.Name) && track.SearchAreaName == gebiet.Name) tracks.TryAdd(track.Id, track);
        }

        var punkte = tracks.Values.SelectMany(t => t.Points).ToList();
        foreach (var team in teams.Where(t => t.IsRunning && t.SearchAreaId == gebiet.Id))
        {
            if (!string.IsNullOrEmpty(team.CollarId)) punkte.AddRange(halsbandVerlauf(team.CollarId));
            punkte.AddRange(handyVerlauf(team.TeamId));
        }

        return punkte;
    }
}

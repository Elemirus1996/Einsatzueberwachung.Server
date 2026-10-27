using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Extensions;
using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>Bericht mit Auswahl (Kartenart, Teams) und Abdeckung live auf der Karte (1.11.6)</summary>
public class BerichtUndAbdeckungTests
{
    private static readonly List<(double Latitude, double Longitude)> Gebiet =
        [(49.3170, 8.4300), (49.3170, 8.4360), (49.3210, 8.4360), (49.3210, 8.4300)];

    private static List<TrackPoint> ZufallsTrack(int seed, int anzahl)
    {
        var r = new Random(seed);
        double lat = 49.318, lon = 8.431;
        var punkte = new List<TrackPoint>();
        for (var i = 0; i < anzahl; i++)
        {
            lat += (r.NextDouble() - 0.45) * 0.0002;
            lon += (r.NextDouble() - 0.45) * 0.0003;
            punkte.Add(new TrackPoint { Latitude = lat, Longitude = lon });
        }
        return punkte;
    }

    [Fact]
    public void Abdeckung_SchnelleRechnungGleichWieJederPunktGegenJedeZelle()
    {
        var punkte = ZufallsTrack(7, 800);
        var schnell = CoverageAnalysisCalculator.CalculateForArea(Gebiet, "A", "#f00", punkte, 25, 15, 4000)!;

        // Vergleich: jede Zelle gegen jeden Punkt (wie vor 1.11.6)
        var erwartet = schnell.Cells.Count(c => punkte.Any(p => Entfernung(c.Latitude, c.Longitude, p.Latitude, p.Longitude) <= 25));

        Assert.Equal(erwartet, schnell.CoveredCells);
        Assert.InRange(schnell.CoveragePercent, 1, 99);
    }

    [Fact]
    public void LiveAbdeckung_AbgeschlosseneSuchenUndLaufendeTracksDesGebiets()
    {
        var gebiet = new SearchArea { Id = "g1", Name = "Waldstück", Coordinates = Gebiet };
        var anderes = new SearchArea { Id = "g2", Name = "Feld", Coordinates = Gebiet };
        var alt = new TeamTrackSnapshot { TeamId = "t1", SearchAreaName = "Waldstück", Points = ZufallsTrack(1, 5) };
        var abgeschlossen = new TeamTrackSnapshot { TeamId = "t1", Points = ZufallsTrack(2, 5) };
        var einsatz = new EinsatzData
        {
            SearchAreas = [gebiet, anderes],
            TrackSnapshots = [alt, abgeschlossen],
            CompletedSearches = [new CompletedSearch { SearchAreaId = "g1", Tracks = [abgeschlossen] }]
        };
        var laeuft = new Team { TeamName = "Rex", CollarId = "c1", SearchAreaId = "g1" };
        laeuft.StartTimer();
        var andersWo = new Team { TeamName = "Luna", CollarId = "c2", SearchAreaId = "g2" };

        var punkte = LiveAbdeckungService.PunkteFuerGebiet(gebiet, einsatz, [laeuft, andersWo],
            id => id == "c1" ? ZufallsTrack(3, 7) : ZufallsTrack(4, 100),
            _ => ZufallsTrack(5, 2));

        Assert.Equal(5 + 5 + 7 + 2, punkte.Count); // alter + abgeschlossener Track (einmal), Halsband + Handy von Rex
        Assert.NotEmpty(LiveAbdeckungService.Berechne(einsatz, [laeuft], _ => [], _ => []));
        laeuft.StopTimer();
    }

    [Fact]
    public void Bericht_TeamauswahlUndKartenart()
    {
        var tracks = new List<TeamTrackSnapshot>
        {
            new() { TeamId = "a", Points = ZufallsTrack(1, 3) },
            new() { TeamId = "b", Points = ZufallsTrack(2, 3) }
        };

        Assert.Equal(2, new PdfBerichtOptionen().WaehleTracks(tracks)!.Count);
        Assert.Equal(["a"], new PdfBerichtOptionen(TeamIds: ["a"]).WaehleTracks(tracks)!.Select(t => t.TeamId));
        Assert.Null(PdfBerichtOptionen.OhneTracks.WaehleTracks(tracks));
        Assert.DoesNotContain(PdfBerichtOptionen.Kartenarten, k => k.Art is MapTileType.SatelliteGoogle or MapTileType.Hybrid);
    }

    [Fact]
    public void Bericht_OptionenAusDerDownloadAdresse()
    {
        var einsatz = new ArchivedEinsatz { TrackSnapshots = [new TeamTrackSnapshot { TeamId = "a", Points = ZufallsTrack(1, 3) }] };

        var standard = DownloadEndpoints.BerichtOptionenAusAdresse(einsatz, null, null, null);
        Assert.True(standard.Tracks);
        Assert.Equal(MapTileType.Streets, standard.Kartenart);
        Assert.Null(standard.TeamIds);

        var auswahl = DownloadEndpoints.BerichtOptionenAusAdresse(einsatz, "topographic", "a, b", null);
        Assert.Equal(MapTileType.Topographic, auswahl.Kartenart);
        Assert.Equal(["a", "b"], auswahl.TeamIds!.OrderBy(x => x));

        Assert.Equal(MapTileType.Streets, DownloadEndpoints.BerichtOptionenAusAdresse(einsatz, "SatelliteGoogle", null, null).Kartenart);
        Assert.False(DownloadEndpoints.BerichtOptionenAusAdresse(einsatz, null, null, "0").Tracks);
    }

    private static double Entfernung(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

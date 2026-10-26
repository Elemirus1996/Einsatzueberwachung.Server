using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Suche eines Teams starten und beenden – gemeinsam fuer Monitor, EL-Ansicht (/el) und API, damit der Suchtrack
/// immer gleich behandelt wird: Start beginnt einen frischen Halsband-Track, Stopp speichert Halsband- und
/// Handy-Track als abgeschlossene Suche. Jede weitere Suche desselben Halsbands bekommt eine andere Farbe.
/// </summary>
public sealed class TeamSucheService
{
    public const string StandardFarbeHalsband = "#FF4444";
    public const string StandardFarbeMensch = "#1976D2";

    /// <summary>
    /// Farben fuer Folgesuchen – kraeftig und auf der Karte gut unterscheidbar. Rot erst am Ende, weil die erste
    /// Suche ohne Suchgebiet schon rot ist (Standardfarbe).
    /// </summary>
    public static readonly IReadOnlyList<string> TrackFarben =
    [
        "#1E88E5", "#43A047", "#FB8C00", "#8E24AA", "#00ACC1",
        "#D81B60", "#6D4C41", "#FDD835", "#3949AB", "#E53935"
    ];

    private readonly IEinsatzService _einsatzService;
    private readonly ICollarTrackingService _collarTrackingService;
    private readonly ITimeService _timeService;

    public TeamSucheService(IEinsatzService einsatzService, ICollarTrackingService collarTrackingService, ITimeService timeService)
    {
        _einsatzService = einsatzService;
        _collarTrackingService = collarTrackingService;
        _timeService = timeService;
    }

    /// <exception cref="InvalidOperationException">z. B. Hund laeuft bereits in einem anderen Team.</exception>
    public async Task StartAsync(string teamId)
    {
        var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);

        // "Weiter" nach manueller Kurzpause: Track laeuft nahtlos weiter. Sonst beginnt eine neue Suche mit frischem Track.
        if (team is { IsManuallyPaused: false } && !string.IsNullOrEmpty(team.CollarId))
        {
            _collarTrackingService.ClearCollarHistory(team.CollarId);
        }

        await _einsatzService.StartTeamTimerAsync(teamId);
    }

    public async Task StopAsync(string teamId)
    {
        var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
        if (team is not null)
        {
            await SaveSearchTracksAsync(team);

            // Halsband nach dem Stopp freigeben (Uebergabe an das naechste Team moeglich)
            if (team.CollarId is not null)
            {
                await _collarTrackingService.UnassignCollarAsync(team.CollarId);
            }
        }

        await _einsatzService.StopTeamTimerAsync(teamId);
    }

    private async Task SaveSearchTracksAsync(Team team)
    {
        var einsatz = _einsatzService.CurrentEinsatz;
        var areaColor = einsatz.SearchAreas.FirstOrDefault(a => a.Id == team.SearchAreaId)?.Color;
        var previous = einsatz.TrackSnapshots ?? [];
        var now = _timeService.Now;

        TeamTrackSnapshot? collarSnapshot = null;
        if (team.CollarId is not null)
        {
            var history = _collarTrackingService.GetLocationHistory(team.CollarId);
            if (history.Count > 0)
            {
                var used = previous
                    .Where(s => s.TrackType == TrackType.CollarTrack && s.CollarId == team.CollarId)
                    .Select(s => s.Color);
                collarSnapshot = new TeamTrackSnapshot
                {
                    CollarId = team.CollarId,
                    CollarName = team.CollarName ?? team.CollarId,
                    TeamId = team.TeamId,
                    TeamName = team.TeamName,
                    SearchAreaName = team.SearchAreaName,
                    Color = ChooseColor(areaColor ?? StandardFarbeHalsband, used),
                    CapturedAt = now,
                    TrackType = TrackType.CollarTrack,
                    Points = history.Select(loc => new TrackPoint
                    {
                        Latitude = loc.Latitude,
                        Longitude = loc.Longitude,
                        Timestamp = loc.Timestamp
                    }).ToList()
                };
            }
        }

        // Laufweg des Hundefuehrers (Handy-GPS)
        TeamTrackSnapshot? humanSnapshot = null;
        var phoneHistory = _einsatzService.GetPhoneTrackHistory(team.TeamId);
        if (phoneHistory.Count >= 2)
        {
            var used = previous
                .Where(s => s.TrackType == TrackType.HumanTrack && s.TeamId == team.TeamId)
                .Select(s => s.Color);
            humanSnapshot = new TeamTrackSnapshot
            {
                CollarId = string.Empty,
                // CollarName dient hier als Anzeigename: Hundefuehrer (oder Teamname)
                CollarName = string.IsNullOrWhiteSpace(team.HundefuehrerName) ? team.TeamName : team.HundefuehrerName,
                TeamId = team.TeamId,
                TeamName = team.TeamName,
                SearchAreaName = team.SearchAreaName,
                Color = ChooseColor(areaColor ?? StandardFarbeMensch, used),
                CapturedAt = now,
                TrackType = TrackType.HumanTrack,
                Points = phoneHistory.Select(loc => new TrackPoint
                {
                    Latitude = loc.Latitude,
                    Longitude = loc.Longitude,
                    Timestamp = loc.Timestamp
                }).ToList()
            };
        }

        if (collarSnapshot is null && humanSnapshot is null)
        {
            return;
        }

        // Halsband- und Handy-Track derselben Suche als eine Episode
        var snapshots = new[] { collarSnapshot, humanSnapshot }.OfType<TeamTrackSnapshot>().ToList();
        var search = await _einsatzService.CreateCompletedSearchAsync(
            team.TeamId,
            snapshots.Min(s => s.Points.Count > 0 ? s.Points[0].Timestamp : s.CapturedAt),
            snapshots.Max(s => s.Points.Count > 0 ? s.Points[^1].Timestamp : s.CapturedAt),
            team.SearchAreaId);

        if (collarSnapshot is not null)
        {
            await _einsatzService.AddTrackToCompletedSearchAsync(search.Id, collarSnapshot);
            // Live-Track von der Karte nehmen – der gespeicherte Track ersetzt ihn
            _collarTrackingService.ClearCollarHistory(collarSnapshot.CollarId);
        }

        if (humanSnapshot is not null)
        {
            await _einsatzService.AddTrackToCompletedSearchAsync(search.Id, humanSnapshot);
            _einsatzService.ClearPhoneTrackHistory(team.TeamId);
        }
    }

    /// <summary>
    /// Erste Suche: Grundfarbe (Suchgebiet oder Standard). Jede weitere Suche: die erste noch nicht benutzte Farbe,
    /// damit sich die Tracks desselben Halsbands auf Karte und im Bericht unterscheiden.
    /// </summary>
    public static string ChooseColor(string baseColor, IEnumerable<string?> usedColors)
    {
        var used = new HashSet<string>(usedColors.OfType<string>(), StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseColor))
        {
            return baseColor;
        }

        var free = TrackFarben.FirstOrDefault(c => !used.Contains(c));
        return free ?? TrackFarben[used.Count % TrackFarben.Count];
    }
}

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Models;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class EinsatzController : ControllerBase
{
    private readonly IEinsatzService _einsatzService;
    private readonly ITimeService _timeService;
    private readonly TeamSucheService _teamSuche;

    public EinsatzController(IEinsatzService einsatzService, ITimeService timeService, TeamSucheService teamSuche)
    {
        _einsatzService = einsatzService;
        _timeService = timeService;
        _teamSuche = teamSuche;
    }

    [HttpGet("current")]
    public IActionResult GetCurrentEinsatz()
    {
        var einsatz = _einsatzService.CurrentEinsatz;
        var hasActiveEinsatz =
            !string.IsNullOrWhiteSpace(einsatz.Einsatzort)
            || !string.IsNullOrWhiteSpace(einsatz.Einsatzleiter)
            || _einsatzService.Teams.Count > 0;

        var response = new CurrentEinsatzResponse(
            hasActiveEinsatz,
            hasActiveEinsatz ? ToEinsatzDto(einsatz) : null,
            _einsatzService.Teams.Count,
            _einsatzService.Teams.Count(t => t.IsRunning));

        return Ok(response);
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartEinsatz([FromBody] StartEinsatzRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Einsatzort) || string.IsNullOrWhiteSpace(request.Einsatzleiter))
        {
            return BadRequest(new ErrorResponse("Einsatzort und Einsatzleiter sind erforderlich."));
        }

        var now = _timeService.Now;
        var einsatz = new EinsatzData
        {
            EinsatzNummer = string.IsNullOrWhiteSpace(request.EinsatzNummer)
                ? $"E-{now:yyyyMMdd-HHmmss}"
                : request.EinsatzNummer.Trim(),
            Einsatzleiter = request.Einsatzleiter.Trim(),
            Fuehrungsassistent = request.Fuehrungsassistent?.Trim() ?? string.Empty,
            Alarmiert = string.IsNullOrWhiteSpace(request.Alarmiert)
                ? now.ToString("dd.MM.yyyy HH:mm")
                : request.Alarmiert.Trim(),
            Einsatzort = request.Einsatzort.Trim(),
            MapAddress = request.Einsatzort.Trim(),
            IstEinsatz = request.IstEinsatz,
            EinsatzDatum = request.EinsatzDatum ?? now,
            AlarmierungsZeit = request.EinsatzDatum ?? now,
            ExportPfad = string.Empty
        };

        await _einsatzService.StartEinsatzAsync(einsatz);

        if (!string.IsNullOrWhiteSpace(request.Bemerkung))
        {
            await _einsatzService.AddGlobalNoteWithSourceAsync(
                request.Bemerkung.Trim(),
                "api",
                "Mobile",
                "Notiz",
                Domain.Models.Enums.GlobalNotesEntryType.Manual,
                "Mobile");
        }

        return Ok(new
        {
            message = "Einsatz gestartet",
            einsatz = ToEinsatzDto(_einsatzService.CurrentEinsatz)
        });
    }

    [HttpGet("teams")]
    public IActionResult GetTeams()
    {
        var teams = _einsatzService.Teams
            .OrderBy(t => t.TeamName)
            .Select(ToTeamDto)
            .ToList();

        return Ok(new { teams });
    }

    [HttpPost("teams/{teamId}/start")]
    public async Task<IActionResult> StartTeamTimer(string teamId)
    {
        try
        {
            await _teamSuche.StartAsync(teamId);
            return Ok(new { message = "Timer gestartet", teamId });
        }
        catch (InvalidOperationException ex)
        {
            // Bekannter Konflikt (z.B. Hund läuft bereits in einem anderen Team) → 409
            return Conflict(new ErrorResponse(ex.Message));
        }
    }

    [HttpPost("teams/{teamId}/stop")]
    public async Task<IActionResult> StopTeamTimer(string teamId)
    {
        await _teamSuche.StopAsync(teamId);
        return Ok(new { message = "Timer gestoppt", teamId });
    }

    private static EinsatzDto ToEinsatzDto(EinsatzData einsatz)
    {
        return new EinsatzDto(
            einsatz.EinsatzNummer,
            einsatz.Einsatzleiter,
            einsatz.Fuehrungsassistent,
            einsatz.Alarmiert,
            einsatz.Einsatzort,
            einsatz.IstEinsatz,
            einsatz.EinsatzDatum,
            einsatz.EinsatzEnde,
            einsatz.SearchAreas.Count,
            einsatz.Teams.Count,
            einsatz.GlobalNotesEntries.Count);
    }

    private static TeamDto ToTeamDto(Team team)
    {
        var status = !team.IsRunning
            ? "Bereit"
            : team.IsSecondWarning
                ? "Kritisch"
                : team.IsFirstWarning
                    ? "Warnung"
                    : "Im Einsatz";

        return new TeamDto(
            team.TeamId,
            team.TeamName,
            team.DogName,
            team.HundefuehrerName,
            team.HelferNamesJoined,
            team.SearchAreaName,
            team.ElapsedTime,
            team.IsRunning,
            team.IsFirstWarning,
            team.IsSecondWarning,
            team.FirstWarningMinutes,
            team.SecondWarningMinutes,
            team.IsDroneTeam,
            team.IsSupportTeam,
            status);
    }

    public sealed record StartEinsatzRequest(
        string Einsatzort,
        string Einsatzleiter,
        string? Fuehrungsassistent,
        string? Alarmiert,
        DateTime? EinsatzDatum,
        string? EinsatzNummer,
        bool IstEinsatz = true,
        string? Bemerkung = null);

    public sealed record CurrentEinsatzResponse(
        bool HasActiveEinsatz,
        EinsatzDto? Einsatz,
        int TeamCount,
        int ActiveTeamCount);

    public sealed record EinsatzDto(
        string EinsatzNummer,
        string Einsatzleiter,
        string Fuehrungsassistent,
        string Alarmiert,
        string Einsatzort,
        bool IstEinsatz,
        DateTime EinsatzDatum,
        DateTime? EinsatzEnde,
        int SearchAreaCount,
        int TeamCount,
        int GlobalNotesCount);

    public sealed record TeamDto(
        string TeamId,
        string TeamName,
        string DogName,
        string HundefuehrerName,
        string HelferName,
        string SearchAreaName,
        TimeSpan ElapsedTime,
        bool IsRunning,
        bool IsFirstWarning,
        bool IsSecondWarning,
        int FirstWarningMinutes,
        int SecondWarningMinutes,
        bool IsDroneTeam,
        bool IsSupportTeam,
        string Status);
}

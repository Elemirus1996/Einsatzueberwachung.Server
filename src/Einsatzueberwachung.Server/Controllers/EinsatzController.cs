using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class EinsatzController : ControllerBase
{
    private readonly IEinsatzService _einsatzService;
    private readonly ILogger<EinsatzController> _logger;

    public EinsatzController(IEinsatzService einsatzService, ILogger<EinsatzController> logger)
    {
        _einsatzService = einsatzService;
        _logger = logger;
    }

    [HttpGet("current")]
    public IActionResult GetCurrentEinsatz()
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen des aktuellen Einsatzes");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartEinsatz([FromBody] StartEinsatzRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Einsatzort) || string.IsNullOrWhiteSpace(request.Einsatzleiter))
        {
            return BadRequest(new { error = "Einsatzort und Einsatzleiter sind erforderlich." });
        }

        try
        {
            var now = DateTime.Now;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Starten des Einsatzes");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("teams")]
    public IActionResult GetTeams()
    {
        try
        {
            var teams = _einsatzService.Teams
                .OrderBy(t => t.TeamName)
                .Select(ToTeamDto)
                .ToList();

            return Ok(new { teams });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Teams");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("teams/{teamId}/start")]
    public async Task<IActionResult> StartTeamTimer(string teamId)
    {
        try
        {
            await _einsatzService.StartTeamTimerAsync(teamId);
            return Ok(new { message = "Timer gestartet", teamId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Starten des Timers fuer Team {TeamId}", teamId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("teams/{teamId}/stop")]
    public async Task<IActionResult> StopTeamTimer(string teamId)
    {
        try
        {
            await _einsatzService.StopTeamTimerAsync(teamId);
            return Ok(new { message = "Timer gestoppt", teamId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Stoppen des Timers fuer Team {TeamId}", teamId);
            return StatusCode(500, new { error = ex.Message });
        }
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
            team.HelferName,
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

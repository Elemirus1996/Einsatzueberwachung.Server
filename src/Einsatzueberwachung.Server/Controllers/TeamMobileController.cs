using System.Security.Claims;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

[ApiController]
[Route("api/team-mobile")]
[IgnoreAntiforgeryToken]
public sealed class TeamMobileController : ControllerBase
{
    private readonly ITeamMobileTokenService _tokenService;
    private readonly IEinsatzService _einsatzService;
    private readonly ICollarTrackingService _collarTrackingService;
    private readonly ILogger<TeamMobileController> _logger;

    public TeamMobileController(
        ITeamMobileTokenService tokenService,
        IEinsatzService einsatzService,
        ICollarTrackingService collarTrackingService,
        ILogger<TeamMobileController> logger)
    {
        _tokenService = tokenService;
        _einsatzService = einsatzService;
        _collarTrackingService = collarTrackingService;
        _logger = logger;
    }

    [HttpGet("teams")]
    [AllowAnonymous] // Team-Anmeldung per Master-Token, ohne Login der Einsatzleitung
    public IActionResult ListTeams([FromQuery] string master)
    {
        if (!_tokenService.ValidateMasterToken(master))
            return Unauthorized(new { error = "Ungueltiger Master-Token oder kein aktiver Einsatz." });

        var einsatz = _einsatzService.CurrentEinsatz;
        if (einsatz is not { IstEinsatz: true } || einsatz.EinsatzEnde != null)
            return NotFound(new { error = "Kein aktiver Einsatz." });

        var teams = _einsatzService.Teams
            .OrderBy(t => t.TeamName)
            .Select(t => new
            {
                teamId = t.TeamId,
                teamName = t.TeamName,
                dogName = t.DogName,
                hundefuehrerName = t.HundefuehrerName,
                hasCollar = !string.IsNullOrWhiteSpace(t.CollarId),
                hasSearchArea = !string.IsNullOrWhiteSpace(t.SearchAreaId)
            })
            .ToList();

        return Ok(new
        {
            einsatzort = einsatz.Einsatzort,
            stichwort = einsatz.Stichwort,
            teams
        });
    }

    [HttpPost("select")]
    [AllowAnonymous] // Team-Anmeldung per Master-Token, ohne Login der Einsatzleitung
    public async Task<IActionResult> SelectTeam([FromBody] TeamMobileSelectRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.MasterToken) || string.IsNullOrWhiteSpace(request.TeamId))
            return BadRequest(new { error = "MasterToken und TeamId erforderlich." });

        if (!_tokenService.ValidateMasterToken(request.MasterToken))
            return Unauthorized(new { error = "Ungueltiger Master-Token." });

        var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == request.TeamId);
        if (team == null)
            return NotFound(new { error = "Team nicht gefunden." });

        var cookieValue = _tokenService.IssueTeamCookieValue(team.TeamId);

        var claims = new[]
        {
            new Claim(TeamMobileAuth.TeamIdClaim, team.TeamId),
            new Claim(TeamMobileAuth.GenerationClaim, _tokenService.CurrentGeneration.ToString()),
            new Claim(TeamMobileAuth.CookieValueClaim, cookieValue)
        };
        var identity = new ClaimsIdentity(claims, TeamMobileAuth.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            TeamMobileAuth.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
            });

        _logger.LogInformation("TeamMobile-Login fuer Team {TeamName} ({TeamId})", team.TeamName, team.TeamId);
        return Ok(new { teamId = team.TeamId, teamName = team.TeamName });
    }

    [HttpPost("select-form")]
    [AllowAnonymous] // Team-Anmeldung per Master-Token, ohne Login der Einsatzleitung
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> SelectTeamForm([FromForm] string master, [FromForm] string teamId)
    {
        var result = await SelectTeam(new TeamMobileSelectRequest { MasterToken = master, TeamId = teamId });
        if (result is OkObjectResult)
            return Redirect("/team");
        return result;
    }

    [HttpPost("logout")]
    [AllowAnonymous] // Team-Anmeldung per Master-Token, ohne Login der Einsatzleitung
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(TeamMobileAuth.AuthenticationScheme);
        return Redirect("/team/login");
    }

    [HttpPost("location")]
    [Authorize(Policy = TeamMobileAuth.AuthorizationPolicy)]
    public async Task<IActionResult> PostLocation([FromBody] TeamPhoneLocationRequest request)
    {
        if (request == null)
            return BadRequest(new { error = "Body fehlt." });

        if (double.IsNaN(request.Lat) || double.IsInfinity(request.Lat) || request.Lat < -90 || request.Lat > 90)
            return BadRequest(new { error = "Ungültiger Breitengrad." });
        if (double.IsNaN(request.Lng) || double.IsInfinity(request.Lng) || request.Lng < -180 || request.Lng > 180)
            return BadRequest(new { error = "Ungültiger Längengrad." });
        if (request.Accuracy.HasValue && (double.IsNaN(request.Accuracy.Value) || double.IsInfinity(request.Accuracy.Value) || request.Accuracy.Value < 0))
            return BadRequest(new { error = "Ungültige Genauigkeit." });

        var teamId = User.FindFirst(TeamMobileAuth.TeamIdClaim)?.Value;
        if (string.IsNullOrWhiteSpace(teamId)) return Unauthorized();

        try
        {
            var timestampUtc = request.Timestamp.HasValue
                ? TraccarProtocol.ParseTimestamp(request.Timestamp.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                : null;
            await _einsatzService.UpdateTeamPhoneLocationAsync(teamId, request.Lat, request.Lng, request.Accuracy, timestampUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TeamMobile-Standort konnte nicht verarbeitet werden (Team {TeamId})", teamId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Standort konnte nicht gespeichert werden." });
        }
        return Ok();
    }

    /// <summary>
    /// Positionen der App „Traccar Client“ (Hintergrund-GPS bei gesperrtem Handy). Die Geraete-ID ist das signierte,
    /// einsatzgebundene Team-Token aus der Team-Ansicht – nach Einsatzende wertlos.
    /// </summary>
    [HttpPost("traccar")]
    [HttpGet("traccar")]
    [AllowAnonymous] // Anmeldung ueber das Team-Token in „id“
    public async Task<IActionResult> PostTraccarPosition()
    {
        var form = Request.HasFormContentType ? await Request.ReadFormAsync() : null;
        string? Value(string key) => form?[key].FirstOrDefault() ?? Request.Query[key].FirstOrDefault();

        var position = TraccarProtocol.Parse(Value);
        if (position is null)
            return BadRequest(new { error = "id, lat und lon erforderlich." });

        // Ungueltiges oder abgelaufenes Token: annehmen und verwerfen. Die App wuerde sonst ihren Puffer endlos
        // wiederholen und den Akku leeren; die Team-Ansicht zeigt den Status ohnehin an.
        if (!_tokenService.TryValidateTeamCookie(position.DeviceId, out var teamId)
            || _einsatzService.Teams.All(t => t.TeamId != teamId))
        {
            _logger.LogInformation("Traccar-Position mit ungueltigem oder abgelaufenem Team-Token verworfen");
            return Ok();
        }

        await _einsatzService.UpdateTeamPhoneLocationAsync(teamId, position.Latitude, position.Longitude, position.Accuracy, position.TimestampUtc);
        return Ok();
    }

    [HttpPost("status")]
    [Authorize(Policy = TeamMobileAuth.AuthorizationPolicy)]
    public async Task<IActionResult> PostStatus([FromBody] TeamMobileStatusRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Status))
            return BadRequest(new { error = "Status fehlt." });

        var teamId = User.FindFirst(TeamMobileAuth.TeamIdClaim)?.Value;
        if (string.IsNullOrWhiteSpace(teamId)) return Unauthorized();

        var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
        if (team == null) return NotFound();

        var text = $"[{team.TeamName}] {request.Status}";
        await _einsatzService.AddGlobalNoteWithSourceAsync(
            text,
            team.TeamId,
            team.TeamName,
            "Mobile",
            Einsatzueberwachung.Domain.Models.Enums.GlobalNotesEntryType.Manual,
            "Team Mobile");

        _logger.LogInformation("TeamMobile-Status von {Team}: {Status}", team.TeamName, request.Status);
        return Ok();
    }

    [HttpGet("state")]
    [Authorize(Policy = TeamMobileAuth.AuthorizationPolicy)]
    public IActionResult GetState()
    {
        var teamId = User.FindFirst(TeamMobileAuth.TeamIdClaim)?.Value;
        if (string.IsNullOrWhiteSpace(teamId)) return Unauthorized();

        var einsatz = _einsatzService.CurrentEinsatz;
        var einsatzActive = einsatz is { IstEinsatz: true } && einsatz.EinsatzEnde == null;

        var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
        if (team == null)
            return NotFound(new { error = "Team nicht mehr vorhanden.", einsatzActive });

        var searchArea = TeamMobileLookup.FindSearchAreaForTeam(einsatz, team);

        IReadOnlyList<CollarLocation> trackPoints = Array.Empty<CollarLocation>();
        CollarLocation? lastLocation = null;
        if (!string.IsNullOrWhiteSpace(team.CollarId))
        {
            trackPoints = _collarTrackingService.GetLocationHistory(team.CollarId);
            lastLocation = trackPoints.Count > 0 ? trackPoints[^1] : null;
        }

        var phoneTrack = _einsatzService.GetPhoneTrackHistory(teamId);

        return Ok(new
        {
            einsatzActive,
            team = new
            {
                teamId = team.TeamId,
                teamName = team.TeamName,
                dogName = team.DogName,
                hundefuehrerName = team.HundefuehrerName,
                helferName = team.HelferNamesJoined,
                isRunning = team.IsRunning,
                collarId = team.CollarId,
                collarName = team.CollarName
            },
            searchArea = searchArea == null ? null : new
            {
                id = searchArea.Id,
                name = searchArea.Name,
                color = searchArea.Color,
                coordinates = searchArea.Coordinates?
                    .Select(c => new { lat = c.Latitude, lng = c.Longitude })
                    .ToList()
            },
            lastLocation = lastLocation == null ? null : new
            {
                lat = lastLocation.Latitude,
                lng = lastLocation.Longitude,
                timestamp = lastLocation.Timestamp
            },
            track = trackPoints
                .Select(p => new { lat = p.Latitude, lng = p.Longitude, timestamp = p.Timestamp })
                .ToList(),
            phoneTrack = phoneTrack
                .Select(p => new { lat = p.Latitude, lng = p.Longitude })
                .ToList()
        });
    }
}

public sealed class TeamMobileSelectRequest
{
    public string MasterToken { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
}

public sealed class TeamMobileStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public sealed class TeamPhoneLocationRequest
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double? Accuracy { get; set; }

    /// <summary>Messzeitpunkt (ms seit 1970, UTC). Gesetzt bei Positionen, die das Handy im Funkloch gepuffert hat.</summary>
    public long? Timestamp { get; set; }
}

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>
/// Schnittstelle der Tracking-App, Rolle Team (#110, verbindlich beschrieben in docs/APP_API.md).
/// Jede Antwort sagt der App, ob sie orten, pausieren oder aufhoeren soll – anders als Traccar versteht die App das.
/// Anmeldung: „Authorization: Bearer &lt;App-Token&gt;“; das Token bekommt die App von der Team-Seite nach der
/// gewohnten Anmeldung per QR-Code.
/// </summary>
[ApiController]
[Route("api/team-mobile/app/v1")]
[IgnoreAntiforgeryToken]
[AllowAnonymous] // eigene Pruefung des App-Tokens
public sealed class TeamMobileAppController : ControllerBase
{
    public const int ApiVersion = 1;
    public const int MaxPositionenProUpload = 500;

    private readonly ITeamMobileTokenService _tokenService;
    private readonly IEinsatzService _einsatzService;
    private readonly ISettingsService _settings;
    private readonly ILogger<TeamMobileAppController> _logger;

    public TeamMobileAppController(ITeamMobileTokenService tokenService, IEinsatzService einsatzService,
        ISettingsService settings, ILogger<TeamMobileAppController> logger)
    {
        _tokenService = tokenService;
        _einsatzService = einsatzService;
        _settings = settings;
        _logger = logger;
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig()
    {
        if (!TryTeam(out var teamId, out var ablaufUtc)) return Gone();

        var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
        var einsatz = _einsatzService.CurrentEinsatz;
        var staffel = await _settings.GetStaffelSettingsAsync();
        var basis = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/team-mobile/app/v1";
        var (intervall, upload) = await AppEinstellungen();

        return Ok(new
        {
            apiVersion = ApiVersion,
            teamId,
            teamName = team?.TeamName ?? "",
            dogName = team?.DogName ?? "",
            einsatz = string.Join(" · ", new[] { einsatz.Stichwort, einsatz.Einsatzort }.Where(s => !string.IsNullOrWhiteSpace(s))),
            staffel = staffel?.StaffelName ?? "",
            positionUrl = basis + "/positions",
            statusUrl = basis + "/status",
            expiresAt = ablaufUtc,
            mode = OrtungsSteuerung.AlsText(OrtungsSteuerung.FuerTeam(einsatz, team)),
            intervalSec = intervall,
            uploadEverySec = upload,
            pauseCheckSec = 120
        });
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        if (!TryTeam(out var teamId, out var ablaufUtc)) return Gone();
        return Ok(await Anweisung(teamId, ablaufUtc));
    }

    [HttpPost("positions")]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> PostPositions([FromBody] AppPositionenRequest? request)
    {
        if (!TryTeam(out var teamId, out var ablaufUtc)) return Gone();

        var positionen = request?.Positions ?? [];
        if (positionen.Count > MaxPositionenProUpload)
            return BadRequest(new { error = $"Höchstens {MaxPositionenProUpload} Positionen pro Upload." });

        var gespeichert = 0;
        foreach (var p in positionen.OrderBy(p => p.Timestamp))
        {
            if (!double.IsFinite(p.Lat) || !double.IsFinite(p.Lng) || Math.Abs(p.Lat) > 90 || Math.Abs(p.Lng) > 180)
                continue;
            DateTime? zeit = p.Timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(p.Timestamp).UtcDateTime : null;
            int? akku = p.Battery is >= 0 and <= 100 ? p.Battery : null;
            // Verwirft selbst, wenn die Suche schon beendet ist (EinsatzService.PhoneLocation)
            await _einsatzService.UpdateTeamPhoneLocationAsync(teamId, p.Lat, p.Lng, p.Accuracy, zeit, akku);
            gespeichert++;
        }

        var antwort = await Anweisung(teamId, ablaufUtc);
        return Ok(new { antwort.mode, antwort.intervalSec, antwort.uploadEverySec, antwort.pauseCheckSec, antwort.expiresAt, accepted = gespeichert });
    }

    private bool TryTeam(out string teamId, out DateTime ablaufUtc)
    {
        teamId = string.Empty;
        ablaufUtc = default;
        var header = Request.Headers.Authorization.ToString();
        const string praefix = "Bearer ";
        if (!header.StartsWith(praefix, StringComparison.OrdinalIgnoreCase)) return false;

        if (!_tokenService.TryValidateAppToken(header[praefix.Length..].Trim(), DateTime.UtcNow, out teamId, out ablaufUtc))
            return false;

        // Team geloescht → wie Einsatzende behandeln
        var id = teamId;
        return _einsatzService.Teams.Any(t => t.TeamId == id);
    }

    private async Task<AppAnweisung> Anweisung(string teamId, DateTime ablaufUtc)
    {
        var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
        var (intervall, upload) = await AppEinstellungen();
        return new AppAnweisung(OrtungsSteuerung.AlsText(OrtungsSteuerung.FuerTeam(_einsatzService.CurrentEinsatz, team)),
            intervall, upload, 120, ablaufUtc);
    }

    private async Task<(int intervall, int upload)> AppEinstellungen()
    {
        var s = await _settings.GetAppSettingsAsync();
        return (Math.Clamp(s.TraccarIntervallSekunden, 5, 300), Math.Clamp(s.AppUploadSekunden, 15, 600));
    }

    // Ungueltig, abgelaufen, Einsatz vorbei oder Team geloescht: App soll endgueltig aufhoeren
    private IActionResult Gone()
    {
        _logger.LogInformation("App-Anfrage mit ungültigem oder abgelaufenem Token – Antwort stop");
        return StatusCode(StatusCodes.Status410Gone, new { mode = "stop" });
    }

    private sealed record AppAnweisung(string mode, int intervalSec, int uploadEverySec, int pauseCheckSec, DateTime expiresAt);

    public sealed class AppPositionenRequest
    {
        public List<AppPosition> Positions { get; set; } = [];
    }

    public sealed class AppPosition
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
        public double? Accuracy { get; set; }
        /// <summary>Unix-Zeit in Millisekunden (Aufnahmezeitpunkt am Handy)</summary>
        public long Timestamp { get; set; }
        public int? Battery { get; set; }
    }
}

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>
/// Schnittstelle der Tracking-App, Rolle Team (#110, verbindlich beschrieben in docs/APP_API.md).
/// Jede Antwort sagt der App, ob sie orten, pausieren oder aufhoeren soll – anders als Traccar versteht die App das.
/// Anmeldung: „Authorization: Bearer &lt;App-Token&gt;“; das Token bekommt die App nativ ueber login/teams und login
/// mit dem Anmelde-Code aus dem bestehenden QR-Code (oder von der Team-Seite in einer WebView).
/// </summary>
[ApiController]
[Route("api/team-mobile/app/v1")]
[IgnoreAntiforgeryToken]
[AllowAnonymous] // eigene Pruefung des App-Tokens
public sealed class TeamMobileAppController : ControllerBase
{
    public const int ApiVersion = 1;
    public const int MaxPositionenProUpload = 500;
    public const int AppTokenStunden = 24;

    private readonly ITeamMobileTokenService _tokenService;
    private readonly IEinsatzService _einsatzService;
    private readonly ISettingsService _settings;
    private readonly ILogger<TeamMobileAppController> _logger;
    private readonly ICollarTrackingService? _halsbaender;
    private readonly TeamAppEreignisse? _ereignisse;
    private readonly AppStatusmeldungen? _statusmeldungen;

    public TeamMobileAppController(ITeamMobileTokenService tokenService, IEinsatzService einsatzService,
        ISettingsService settings, ILogger<TeamMobileAppController> logger,
        ICollarTrackingService? halsbaender = null, TeamAppEreignisse? ereignisse = null, AppStatusmeldungen? statusmeldungen = null)
    {
        _tokenService = tokenService;
        _einsatzService = einsatzService;
        _settings = settings;
        _logger = logger;
        _halsbaender = halsbaender;
        _ereignisse = ereignisse;
        _statusmeldungen = statusmeldungen;
    }

    // ── Native Team-Ansicht (#119, docs/APP_API.md Abschnitte 6–9) ───────────

    /// <summary>Statusmeldungen der App: fester Code → Text wie die Knoepfe der Team-Seite</summary>
    public static readonly IReadOnlyDictionary<string, string> StatusmeldungCodes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["im_gebiet"] = "im Suchgebiet eingetroffen",
        ["pause"] = "Pause",
        ["funk"] = "Funkkontakt benötigt",
        ["beendet"] = "Auftrag beendet"
    };

    [HttpGet("lage")]
    public async Task<IActionResult> GetLage()
    {
        if (!TryTeam(out var teamId, out _) || _halsbaender is null) return Gone();
        var team = _einsatzService.Teams.First(t => t.TeamId == teamId);
        var einsatz = _einsatzService.CurrentEinsatz;
        var staffel = (await _settings.GetStaffelSettingsAsync())?.StaffelName ?? "";
        return Ok(AppLage.Erstellen(einsatz, team, staffel, _halsbaender, _einsatzService.TeamMitteilungen(teamId),
            OrtungsSteuerung.AlsText(OrtungsSteuerung.FuerTeam(einsatz, team))));
    }

    [HttpGet("tracks")]
    public IActionResult GetTracks([FromQuery] long? seit)
    {
        if (!TryTeam(out var teamId, out _) || _halsbaender is null) return Gone();
        var team = _einsatzService.Teams.First(t => t.TeamId == teamId);
        return Ok(AppLage.Tracks(_einsatzService.CurrentEinsatz, team, _einsatzService, _halsbaender, seit));
    }

    [HttpPost("statusmeldung")]
    public async Task<IActionResult> PostStatusmeldung([FromBody] AppStatusmeldungRequest? request)
    {
        if (!TryTeam(out var teamId, out _) || _statusmeldungen is null) return Gone();
        if (request?.Code is null || !StatusmeldungCodes.TryGetValue(request.Code, out var text))
            return BadRequest(new { error = "Unbekannter Code.", codes = StatusmeldungCodes.Keys });
        if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 64)
            return BadRequest(new { error = "id fehlt oder ist zu lang (höchstens 64 Zeichen)." });

        var jetzt = DateTime.UtcNow;
        if (!_statusmeldungen.ErstesMal(teamId, request.Id, jetzt))
            return Ok(new { zeit = jetzt, doppelt = true });

        var team = _einsatzService.Teams.First(t => t.TeamId == teamId);
        await _einsatzService.AddGlobalNoteWithSourceAsync($"[{team.TeamName}] {text}", team.TeamId, team.TeamName,
            "Mobile", Einsatzueberwachung.Domain.Models.Enums.GlobalNotesEntryType.Manual, "Team-App");
        return Ok(new { zeit = jetzt, doppelt = false });
    }

    /// <summary>Live-Ereignisse als Server-Sent Events; Token nur im Header</summary>
    [HttpGet("ereignisse")]
    public async Task GetEreignisse()
    {
        if (!TryTeam(out var teamId, out _) || _ereignisse is null)
        {
            Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status410Gone;
            await Response.WriteAsJsonAsync(new { mode = "stop" });
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // nginx nicht puffern
        await Response.WriteAsync("retry: 5000\n\n");
        await Response.Body.FlushAsync();

        var (id, leser) = _ereignisse.Abonnieren(teamId);
        var abbruch = HttpContext.RequestAborted;
        try
        {
            while (!abbruch.IsCancellationRequested)
            {
                using var takt = CancellationTokenSource.CreateLinkedTokenSource(abbruch);
                takt.CancelAfter(PingIntervall);
                string? text = null;
                try
                {
                    if (await leser.WaitToReadAsync(takt.Token)) leser.TryRead(out text);
                    else break; // abgemeldet (stop)
                }
                catch (OperationCanceledException) when (!abbruch.IsCancellationRequested)
                {
                    text = ": ping\n\n";
                }
                if (text is null) continue;
                await Response.WriteAsync(text, abbruch);
                await Response.Body.FlushAsync(abbruch);
                if (text.StartsWith("event: stop", StringComparison.Ordinal)) break;
            }
        }
        catch (OperationCanceledException) { /* App hat die Verbindung getrennt */ }
        finally
        {
            _ereignisse.Abmelden(id);
        }
    }

    public static TimeSpan PingIntervall { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Native Anmeldung, Schritt 1: Anmelde-Code aus dem QR-Code → Einsatz und Teamliste.</summary>
    [HttpPost("login/teams")]
    public async Task<IActionResult> LoginTeams([FromBody] AppLoginRequest? request)
    {
        if (!AnmeldeCodeGueltig(request?.Code)) return AnmeldungAbgelehnt();

        var staffel = await _settings.GetStaffelSettingsAsync();
        return Ok(new
        {
            apiVersion = ApiVersion,
            einsatz = EinsatzText(_einsatzService.CurrentEinsatz),
            uebung = !_einsatzService.CurrentEinsatz.IstEinsatz,
            staffel = staffel?.StaffelName ?? "",
            teams = _einsatzService.Teams
                .OrderBy(t => t.TeamName)
                .Select(t => new
                {
                    teamId = t.TeamId,
                    teamName = t.TeamName,
                    dogName = t.DogName ?? "",
                    hundefuehrerName = t.HundefuehrerName ?? ""
                })
                .ToList()
        });
    }

    /// <summary>Native Anmeldung, Schritt 2: Anmelde-Code + Team → App-Token (gleiche Regeln wie /team/login).</summary>
    [HttpPost("login")]
    public IActionResult Login([FromBody] AppLoginRequest? request)
    {
        if (!AnmeldeCodeGueltig(request?.Code)) return AnmeldungAbgelehnt();

        var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == request!.TeamId);
        if (team == null || team.TeamId.Contains('.'))
            return NotFound(new { error = "Team nicht gefunden." });

        var ablaufUtc = DateTime.UtcNow.AddHours(AppTokenStunden);
        _logger.LogInformation("App-Anmeldung fuer Team {TeamName} ({TeamId})", team.TeamName, team.TeamId);
        return Ok(new
        {
            apiVersion = ApiVersion,
            rolle = "team",
            token = _tokenService.IssueAppToken(team.TeamId, ablaufUtc),
            teamId = team.TeamId,
            teamName = team.TeamName,
            expiresAt = ablaufUtc
        });
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
            einsatz = EinsatzText(einsatz),
            uebung = !einsatz.IstEinsatz,
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

    // Wie /team/login: nur mit gueltigem Anmelde-Code und waehrend eines laufenden Einsatzes oder einer Uebung
    private bool AnmeldeCodeGueltig(string? code)
    {
        var bereinigt = (code ?? "").Replace(" ", "").ToUpperInvariant();
        if (!_tokenService.ValidateMasterToken(bereinigt)) return false;
        return _einsatzService.CurrentEinsatz is { EinsatzEnde: null } einsatz && !string.IsNullOrWhiteSpace(einsatz.Einsatzort);
    }

    private IActionResult AnmeldungAbgelehnt() =>
        Unauthorized(new { error = "Der Anmelde-Code ist ungültig oder es ist gerade kein Einsatz aktiv." });

    private static string EinsatzText(Domain.Models.EinsatzData einsatz) =>
        string.Join(" · ", new[] { einsatz.Stichwort, einsatz.Einsatzort }.Where(s => !string.IsNullOrWhiteSpace(s)));

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

    public sealed class AppStatusmeldungRequest
    {
        /// <summary>Von der App erzeugte Kennung (z. B. UUID), entdoppelt Wiederholungen im Funkloch</summary>
        public string? Id { get; set; }
        public string? Code { get; set; }
    }

    public sealed class AppLoginRequest
    {
        /// <summary>Anmelde-Code aus dem QR-Code (…/team/login/&lt;Code&gt;)</summary>
        public string? Code { get; set; }
        public string? TeamId { get; set; }
    }

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

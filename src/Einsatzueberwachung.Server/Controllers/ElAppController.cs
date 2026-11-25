using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Security;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>
/// Schnittstelle der Tracking-App, Rolle Einsatzleitung (#110, docs/APP_API.md): Standort der Einsatzleitung fuer
/// die Karte im ELW. Nur intern (VPN) – am oeffentlichen Port gesperrt, zusaetzlich hier geprueft.
/// Anmeldung: „Authorization: Bearer &lt;EL-Token&gt;“ aus der angemeldeten EL-Ansicht.
/// </summary>
[ApiController]
[Route("api/el/app/v1")]
[IgnoreAntiforgeryToken]
[AllowAnonymous] // eigene Pruefung des EL-Tokens
public sealed partial class ElAppController(
    ElAppTokenService tokens, ElPositionService positionen, LeitungsGeraetNamen geraete, PublicAccess publicAccess,
    ILogger<ElAppController> logger, AppLogo? logo = null,
    // Native Einsatzleitung (#125, ElAppController.Native.cs)
    LeitungAuthStore? leitung = null, LoginLockout? sperre = null, Einsatzueberwachung.Domain.Interfaces.IAuditLogService? audit = null,
    Einsatzueberwachung.Domain.Interfaces.IEinsatzService? einsatz = null, Einsatzueberwachung.Domain.Interfaces.ICollarTrackingService? halsbaender = null,
    Einsatzueberwachung.Domain.Interfaces.ISettingsService? einstellungen = null, ServerKennung? kennung = null,
    ElAppEreignisse? ereignisse = null,
    // Stufe 3 (ElAppController.Aktionen.cs)
    ElAppAktionen? aktionen = null, TeamSucheService? suche = null, Einsatzueberwachung.Server.Services.Radio.IRadioService? funk = null,
    Einsatzueberwachung.Domain.Interfaces.ITeamMobileTokenService? teamNachrichten = null,
    // Stufe 4b (ElAppController.Karte.cs)
    LiveAbdeckungService? abdeckungen = null,
    // Stufe 5 (ElAppController.Einsatz.cs)
    EinsatzAbschlussService? abschluss = null, Einsatzueberwachung.Domain.Interfaces.IMasterDataService? stammdaten = null,
    Einsatzueberwachung.Domain.Interfaces.IDiveraService? divera = null, Einsatzueberwachung.Domain.Interfaces.ITimeService? zeit = null,
    // Navigationsziele (Variante C, ElAppController.Aktionen.cs)
    Navigationspunkte? navigationspunkte = null,
    // Bericht teilen (Idee 6.2, ElAppController.Einsatz.cs)
    Einsatzueberwachung.Domain.Interfaces.IArchivService? archiv = null, Einsatzueberwachung.Domain.Interfaces.IPdfExportService? pdfExport = null,
    // Mitteilungen im Hintergrund (#79, ElAppController.Mitteilungen.cs)
    ElMitteilungen? mitteilungen = null) : ControllerBase
{
    /// <summary>Staffellogo als PNG (docs/APP_API.md, Einsatzleitung Abschnitt 3) – nur intern</summary>
    [HttpGet("logo")]
    public async Task<IActionResult> GetLogo()
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        if (!tokens.TryPruefen(Bearer(), DateTime.UtcNow, out _) || logo is null) return Gone();
        return await AppLogoAntwort.ErstellenAsync(logo, Request, Response);
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        if (!tokens.TryPruefen(Bearer(), DateTime.UtcNow, out var ablaufUtc, out _, out var personId)) return Gone();
        return Ok(Anweisung(ablaufUtc, await IchAsync(personId)));
    }

    [HttpPost("positions")]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> PostPositions([FromBody] TeamMobileAppController.AppPositionenRequest? request)
    {
        if (publicAccess.IsPublicRequest(HttpContext)) return NotFound();
        if (!tokens.TryPruefen(Bearer(), DateTime.UtcNow, out var ablaufUtc, out _, out var personId)) return Gone();
        var ich = await IchAsync(personId);

        var liste = request?.Positions ?? [];
        if (liste.Count > TeamMobileAppController.MaxPositionenProUpload)
            return BadRequest(new { error = $"Höchstens {TeamMobileAppController.MaxPositionenProUpload} Positionen pro Upload." });

        // Geraetename aus dem VPN (z. B. „Tablet EL“) – so sieht der Fuehrungsassistent, wer wo ist
        var geraet = geraete.NameFuer(HttpContext.Connection.RemoteIpAddress) ?? LeitungsGeraetNamen.Einsatzleitung;
        // Nur der Einsatzleiter meldet seinen Standort (APP_API EL Abschnitt 4a); sonst verwerfen
        var angenommen = !ElRolle.SendetStandort(ich.Rolle) ? 0 : positionen.Melden(geraet, liste.Select(p => (p.Lat, p.Lng, p.Accuracy,
            p.Timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(p.Timestamp).UtcDateTime : DateTime.UtcNow, p.Battery)));

        var antwort = Anweisung(ablaufUtc, ich);
        return Ok(new { antwort.mode, antwort.intervalSec, antwort.uploadEverySec, antwort.pauseCheckSec, antwort.expiresAt, accepted = angenommen, antwort.mitteilungStand });
    }

    private string? Bearer()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }

    // Kein Einsatz: GPS aus, aber angemeldet bleiben – beim naechsten Einsatz geht es ohne Eingabe weiter.
    // Mit Mitteilungen (#79) alle 30 s hochladen, damit Warnungen spaetestens nach einer halben Minute ankommen.
    private ElAnweisung Anweisung(DateTime ablaufUtc, ElIch ich) =>
        new(OrtungsSteuerung.AlsText(ModusFuer(ich, positionen.Modus)), 30, mitteilungen is null ? 60 : 30, 120, ablaufUtc, mitteilungen?.Stand);

    private IActionResult Gone()
    {
        logger.LogInformation("EL-App-Anfrage mit ungültigem oder abgelaufenem Token – Antwort stop");
        return StatusCode(StatusCodes.Status410Gone, new { mode = "stop" });
    }

    private sealed record ElAnweisung(string mode, int intervalSec, int uploadEverySec, int pauseCheckSec, DateTime expiresAt, string? mitteilungStand);
}

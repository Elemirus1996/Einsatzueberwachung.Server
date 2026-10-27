using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Server.Models;
using Einsatzueberwachung.Server.Services;
using Einsatzueberwachung.Server.Services.Radio;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

/// <summary>
/// Nimmt Funksprueche und EL-Notizen an, die die Einsatzleitung im Funkloch auf dem Geraet erfasst hat
/// (js/el-outbox.js). Nur mit Leitungs-Anmeldung (Fallback-Policy).
/// </summary>
[ApiController]
[Route("api/el-outbox")]
[Produces("application/json")]
public sealed class ElOutboxController : ControllerBase
{
    private readonly ElOutboxService _outbox;
    private readonly IRadioService _radioService;
    private readonly IEinsatzService _einsatzService;
    private readonly ITimeService _timeService;
    private readonly LeitungsGeraetNamen _geraete;

    public ElOutboxController(ElOutboxService outbox, IRadioService radioService, IEinsatzService einsatzService, ITimeService timeService,
        LeitungsGeraetNamen geraete)
    {
        _geraete = geraete;
        _outbox = outbox;
        _radioService = radioService;
        _einsatzService = einsatzService;
        _timeService = timeService;
    }

    [HttpPost]
    public async Task<IActionResult> Deliver([FromBody] ElOutboxEntry entry, CancellationToken cancellationToken)
    {
        var error = ElOutboxService.Validate(entry);
        if (error is not null)
        {
            return BadRequest(new ErrorResponse(error));
        }

        var einsatz = _einsatzService.CurrentEinsatz;
        if (string.IsNullOrWhiteSpace(einsatz.Einsatzort) || einsatz.EinsatzEnde is not null)
        {
            return Conflict(new ErrorResponse("Kein laufender Einsatz – Eintrag konnte nicht zugeordnet werden."));
        }

        var now = DateTimeOffset.UtcNow;
        if (!_outbox.TryClaim(entry.Id, now))
        {
            return Ok(new { duplicate = true });
        }

        try
        {
            var text = ElOutboxService.BuildText(entry, now, _timeService.Now);
            if (entry.Kind == "funk")
            {
                await _radioService.AddMessageAsync(
                    new CreateRadioMessageRequest(text, "einsatzleitung", Absender, Absender),
                    cancellationToken);
            }
            else
            {
                await _einsatzService.AddElNotizAsync(text);
            }
        }
        catch
        {
            _outbox.Release(entry.Id);
            throw;
        }

        return Ok(new { duplicate = false });
    }

    private string Absender => LeitungsGeraetNamen.Absender(_geraete.NameFuer(HttpContext.Connection.RemoteIpAddress));
}

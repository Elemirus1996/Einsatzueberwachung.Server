// REST-API Endpunkt für GPS-Halsband-Daten
// Empfängt Live-Positionen von der externen Halsband-Software (bis zu 20 Halsbänder gleichzeitig)

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CollarWebhookController : ControllerBase
{
    private readonly ICollarTrackingService _trackingService;
    private readonly ILogger<CollarWebhookController> _logger;

    public CollarWebhookController(ICollarTrackingService trackingService, ILogger<CollarWebhookController> logger)
    {
        _trackingService = trackingService;
        _logger = logger;
    }

    /// <summary>
    /// Empfängt eine neue GPS-Position von der externen Halsband-Software.
    /// </summary>
    [HttpPost("location")]
    public async Task<IActionResult> ReceiveLocation([FromBody] CollarLocationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            return BadRequest(new { error = "Id ist erforderlich." });
        }

        if (request.Coordinates == null)
        {
            return BadRequest(new { error = "Coordinates sind erforderlich." });
        }

        try
        {
            var location = await _trackingService.ReceiveLocationAsync(
                request.Id,
                request.CollarName ?? request.Id,
                request.Coordinates.Lat,
                request.Coordinates.Lng);

            return Ok(new
            {
                message = "Position empfangen",
                collarId = request.Id,
                timestamp = location.Timestamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Empfangen der GPS-Position für Halsband {CollarId}", request.Id);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gibt alle bekannten Halsbänder zurück.
    /// </summary>
    [HttpGet("collars")]
    public IActionResult GetCollars()
    {
        var collars = _trackingService.Collars;
        return Ok(new { collars });
    }

    /// <summary>
    /// Gibt die nicht zugewiesenen Halsbänder zurück.
    /// </summary>
    [HttpGet("collars/available")]
    public IActionResult GetAvailableCollars()
    {
        var collars = _trackingService.GetAvailableCollars();
        return Ok(new { collars });
    }

    /// <summary>
    /// Gibt den Positionsverlauf eines Halsbands zurück.
    /// </summary>
    [HttpGet("history/{collarId}")]
    public IActionResult GetLocationHistory(string collarId)
    {
        var history = _trackingService.GetLocationHistory(collarId);
        return Ok(new { collarId, locations = history });
    }

    // --- Request-DTOs ---

    public sealed record CollarLocationRequest(
        string Id,
        string? CollarName,
        CoordinatesDto Coordinates);

    public sealed record CoordinatesDto(double Lat, double Lng);
}

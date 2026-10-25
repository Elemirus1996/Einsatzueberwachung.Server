// REST-API Endpunkt für GPS-Halsband-Daten
// Empfängt Live-Positionen von der externen Halsband-Software (bis zu 20 Halsbänder gleichzeitig)

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CollarWebhookController : ControllerBase
{
    private readonly ICollarTrackingService _trackingService;

    public CollarWebhookController(ICollarTrackingService trackingService)
    {
        _trackingService = trackingService;
    }

    /// <summary>
    /// Empfängt eine neue GPS-Position von der externen Halsband-Software.
    /// </summary>
    [HttpPost("location")]
    [AllowAnonymous] // LiveTracking-App; Absicherung per API-Schluessel folgt in 1.10 (Teil 2)
    public async Task<IActionResult> ReceiveLocation([FromBody] CollarLocationRequest request)
    {
        if (request is null)
        {
            return BadRequest(new ErrorResponse("Body ist erforderlich."));
        }

        if (string.IsNullOrWhiteSpace(request.Id))
        {
            return BadRequest(new ErrorResponse("Id ist erforderlich."));
        }

        if (!IsValidCollarId(request.Id))
        {
            return BadRequest(new ErrorResponse($"Id ist ungültig (höchstens {MaxIdLength} Zeichen: Buchstaben, Ziffern, - _ . :)."));
        }

        if (request.CollarName is { } name && (name.Length > MaxNameLength || name.Any(char.IsControl)))
        {
            return BadRequest(new ErrorResponse($"CollarName ist ungültig (höchstens {MaxNameLength} Zeichen, keine Steuerzeichen)."));
        }

        // Unbegrenzt neue Halsband-IDs wuerden Speicher und Karte fluten
        if (_trackingService.Collars.Count >= MaxCollars && _trackingService.Collars.All(c => c.Id != request.Id))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ErrorResponse($"Es sind bereits {MaxCollars} Halsbänder bekannt."));
        }

        if (request.Coordinates == null)
        {
            return BadRequest(new ErrorResponse("Coordinates sind erforderlich."));
        }

        if (double.IsNaN(request.Coordinates.Lat) || double.IsInfinity(request.Coordinates.Lat) ||
            request.Coordinates.Lat < -90 || request.Coordinates.Lat > 90)
        {
            return BadRequest(new ErrorResponse("Coordinates.Lat ist ungültig."));
        }

        if (double.IsNaN(request.Coordinates.Lng) || double.IsInfinity(request.Coordinates.Lng) ||
            request.Coordinates.Lng < -180 || request.Coordinates.Lng > 180)
        {
            return BadRequest(new ErrorResponse("Coordinates.Lng ist ungültig."));
        }

        var location = await _trackingService.ReceiveLocationAsync(
            request.Id,
            request.CollarName ?? request.Id,
            request.Coordinates.Lat,
            request.Coordinates.Lng,
            request.BatteryLevel);

        return Ok(new
        {
            message = "Position empfangen",
            collarId = request.Id,
            timestamp = location.Timestamp
        });
    }

    /// <summary>
    /// Gibt alle bekannten Halsbänder zurück.
    /// </summary>
    [HttpGet("collars")]
    [AllowAnonymous] // Verbindungstest der LiveTracking-App; API-Schluessel folgt in 1.10 (Teil 2)
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

    // --- Eingabepruefung (Audit 2026-10-24: Werte landen in Karten-Popups und PDF) ---

    public const int MaxIdLength = 64;
    public const int MaxNameLength = 64;
    public const int MaxCollars = 50;

    public static bool IsValidCollarId(string id) =>
        id.Length <= MaxIdLength && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');

    // --- Request-DTOs ---

    public sealed record CollarLocationRequest(
        string Id,
        string? CollarName,
        CoordinatesDto Coordinates,
        int? BatteryLevel);

    public sealed record CoordinatesDto(double Lat, double Lng);
}

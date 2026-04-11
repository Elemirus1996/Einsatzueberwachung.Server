// Divera 24/7 REST-Controller
// Endpunkte: /api/divera/...
// Nutzbar von der Mobile-App (MobileApiClient) und anderen externen Clients

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

[ApiController]
[Route("api/divera")]
[Produces("application/json")]
public class DiveraController : ControllerBase
{
    private readonly IDiveraService _diveraService;
    private readonly IEinsatzService _einsatzService;
    private readonly ITimeService _timeService;
    private readonly ILogger<DiveraController> _logger;

    public DiveraController(
        IDiveraService diveraService,
        IEinsatzService einsatzService,
        ITimeService timeService,
        ILogger<DiveraController> logger)
    {
        _diveraService = diveraService;
        _einsatzService = einsatzService;
        _timeService = timeService;
        _logger = logger;
    }

    /// <summary>
    /// Gibt den Verbindungsstatus und ob Divera konfiguriert ist zurueck
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        if (!_diveraService.IsConfigured)
        {
            return Ok(new
            {
                configured = false,
                connected = false,
                message = "Divera 24/7 ist nicht konfiguriert. Bitte Einheiten-Zugangscode in appsettings.json eintragen."
            });
        }

        var connected = await _diveraService.TestConnectionAsync();
        return Ok(new
        {
            configured = true,
            connected,
            message = connected
                ? "Verbindung zu Divera 24/7 erfolgreich"
                : "Verbindung zu Divera 24/7 fehlgeschlagen. Bitte Einheiten-Zugangscode pruefen."
        });
    }

    /// <summary>
    /// Gibt alle aktiven Alarme von Divera zurueck
    /// </summary>
    [HttpGet("alarms")]
    public async Task<IActionResult> GetActiveAlarms()
    {
        if (!_diveraService.IsConfigured)
            return Ok(new { configured = false, alarms = Array.Empty<object>() });

        try
        {
            var alarms = await _diveraService.GetActiveAlarmsAsync();
            return Ok(new { configured = true, alarms });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Divera-Alarme");
            return StatusCode(500, new { error = "Fehler beim Abrufen der Divera-Alarme" });
        }
    }

    /// <summary>
    /// Gibt einen einzelnen Alarm anhand der ID zurueck (inkl. Rueckmeldungen)
    /// </summary>
    [HttpGet("alarms/{id:int}")]
    public async Task<IActionResult> GetAlarm(int id)
    {
        if (!_diveraService.IsConfigured)
            return Ok(new { configured = false, alarm = (object?)null });

        try
        {
            var alarm = await _diveraService.GetAlarmByIdAsync(id);
            if (alarm == null)
                return NotFound(new { error = $"Alarm {id} nicht gefunden" });

            return Ok(new { configured = true, alarm });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen von Divera-Alarm {Id}", id);
            return StatusCode(500, new { error = "Fehler beim Abrufen des Alarms" });
        }
    }

    /// <summary>
    /// Gibt alle Mitglieder mit ihrem aktuellen Verfuegbarkeitsstatus zurueck
    /// </summary>
    [HttpGet("members")]
    public async Task<IActionResult> GetMembers()
    {
        if (!_diveraService.IsConfigured)
            return Ok(new { configured = false, members = Array.Empty<object>() });

        try
        {
            var members = await _diveraService.GetMembersWithStatusAsync();
            return Ok(new { configured = true, members });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Divera-Mitglieder");
            return StatusCode(500, new { error = "Fehler beim Abrufen der Mitglieder" });
        }
    }

    /// <summary>
    /// Gibt nur die aktuell verfuegbaren Mitglieder zurueck (StatusId 1 oder 2)
    /// </summary>
    [HttpGet("members/available")]
    public async Task<IActionResult> GetAvailableMembers()
    {
        if (!_diveraService.IsConfigured)
            return Ok(new { configured = false, members = Array.Empty<object>() });

        try
        {
            var members = await _diveraService.GetAvailableMembersAsync();
            return Ok(new { configured = true, members });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der verfuegbaren Divera-Mitglieder");
            return StatusCode(500, new { error = "Fehler beim Abrufen der verfuegbaren Mitglieder" });
        }
    }

    /// <summary>
    /// Importiert einen Divera-Alarm als neuen Einsatz.
    /// Befuellt EinsatzData aus den Alarm-Details und ruft StartEinsatzAsync auf.
    /// </summary>
    [HttpPost("import/{alarmId:int}")]
    public async Task<IActionResult> ImportAlarmAsEinsatz(int alarmId)
    {
        if (!_diveraService.IsConfigured)
            return BadRequest(new { error = "Divera 24/7 ist nicht konfiguriert." });

        try
        {
            var alarm = await _diveraService.GetAlarmByIdAsync(alarmId);
            if (alarm == null)
                return NotFound(new { error = $"Divera-Alarm {alarmId} nicht gefunden." });

            if (alarm.Closed)
                return BadRequest(new { error = "Dieser Alarm ist bereits geschlossen und kann nicht importiert werden." });

            var now = _timeService.Now;

            var einsatz = new EinsatzData
            {
                EinsatzNummer = alarm.ForeignId?.Trim() is { Length: > 0 } fid
                    ? fid
                    : $"DIV-{alarmId}",
                Alarmiert = "Divera 24/7",
                Einsatzort = alarm.Address,
                MapAddress = alarm.Address,
                IstEinsatz = true,
                EinsatzDatum = alarm.Date > DateTime.MinValue ? alarm.Date : now,
                AlarmierungsZeit = alarm.Date > DateTime.MinValue ? alarm.Date : now,
                ExportPfad = string.Empty
            };

            // GPS-Koordinaten uebernehmen falls vorhanden
            if (alarm.Lat.HasValue && alarm.Lng.HasValue)
            {
                einsatz.ElwPosition = (alarm.Lat.Value, alarm.Lng.Value);
            }

            await _einsatzService.StartEinsatzAsync(einsatz);

            // Alarm-Details als Notiz hinzufuegen
            var notizText = $"Divera-Alarm #{alarmId}: {alarm.Title}";
            if (!string.IsNullOrWhiteSpace(alarm.Text))
                notizText += $"\n{alarm.Text}";

            await _einsatzService.AddGlobalNoteWithSourceAsync(
                notizText,
                "divera",
                "Divera 24/7",
                "Import",
                GlobalNotesEntryType.System,
                "Divera");

            _logger.LogInformation("Divera-Alarm {AlarmId} als Einsatz importiert: {Einsatzort}", alarmId, alarm.Address);

            return Ok(new
            {
                message = "Divera-Alarm erfolgreich als Einsatz importiert",
                einsatz = new
                {
                    einsatz.EinsatzNummer,
                    einsatz.Einsatzort,
                    einsatz.AlarmierungsZeit
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Importieren von Divera-Alarm {AlarmId}", alarmId);
            return StatusCode(500, new { error = "Fehler beim Importieren des Alarms: " + ex.Message });
        }
    }
}

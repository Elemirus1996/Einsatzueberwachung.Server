using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Models;
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

    /// <summary>Verbindungstest — gibt configured/connected/message zurueck.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var configured = _diveraService.IsConfigured;
        if (!configured)
        {
            return Ok(new { configured = false, connected = false, message = "Divera nicht konfiguriert. Bitte API-Key in den Einstellungen hinterlegen." });
        }

        var connected = await _diveraService.TestConnectionAsync();
        return Ok(new
        {
            configured = true,
            connected,
            message = connected ? "Verbindung zu Divera 24/7 erfolgreich." : "Verbindung fehlgeschlagen. Bitte API-Key prüfen."
        });
    }

    /// <summary>Aktive (nicht geschlossene) Alarme.</summary>
    [HttpGet("alarms")]
    public async Task<IActionResult> GetAlarms()
    {
        var alarms = await _diveraService.GetActiveAlarmsAsync();
        return Ok(alarms);
    }

    /// <summary>Einzelner Alarm nach ID.</summary>
    [HttpGet("alarms/{id:int}")]
    public async Task<IActionResult> GetAlarmById(int id)
    {
        var alarm = await _diveraService.GetAlarmByIdAsync(id);
        if (alarm == null)
            return NotFound(new ErrorResponse($"Alarm {id} nicht gefunden."));

        return Ok(alarm);
    }

    /// <summary>Alle Mitglieder mit Verfuegbarkeitsstatus.</summary>
    [HttpGet("members")]
    public async Task<IActionResult> GetMembers()
    {
        var members = await _diveraService.GetMembersWithStatusAsync();
        return Ok(members);
    }

    /// <summary>Nur verfuegbare Mitglieder (Status 1 oder 2).</summary>
    [HttpGet("members/available")]
    public async Task<IActionResult> GetAvailableMembers()
    {
        var members = await _diveraService.GetAvailableMembersAsync();
        return Ok(members);
    }

    /// <summary>Gesamtabfrage pull/all — Alarme und Mitglieder in einem Call.</summary>
    [HttpGet("pull")]
    public async Task<IActionResult> PullAll()
    {
        var pull = await _diveraService.PullAllAsync();
        if (pull == null)
            return Ok(new { alarms = Array.Empty<object>(), members = Array.Empty<object>(), configured = false });

        return Ok(pull);
    }

    /// <summary>
    /// Diagnose: Gibt die rohen JSON-Antworten beider Divera-Endpunkte zurueck.
    /// Aufruf: GET /api/divera/debug — zeigt was Divera tatsaechlich sendet.
    /// </summary>
    [HttpGet("debug")]
    public async Task<IActionResult> GetDebug([FromServices] IWebHostEnvironment environment)
    {
        // Rohdaten von Divera (Namen, Telefonnummern, Alarmtexte) nur in der Entwicklungsumgebung
        if (!environment.IsDevelopment())
            return NotFound();

        var raw = await _diveraService.GetRawDiagnosticAsync();
        return Ok(raw);
    }

    /// <summary>Alarm als neuen Einsatz importieren.</summary>
    [HttpPost("import/{alarmId:int}")]
    public async Task<IActionResult> ImportAlarmAsEinsatz(int alarmId)
    {
        var alarm = await _diveraService.GetAlarmByIdAsync(alarmId);
        if (alarm == null)
            return NotFound(new ErrorResponse($"Divera-Alarm {alarmId} nicht gefunden."));

        var now = _timeService.Now;
        var einsatzData = new EinsatzData
        {
            EinsatzNummer = $"D-{alarm.Id}",
            Einsatzort = alarm.Address,
            MapAddress = alarm.Address,
            Stichwort = alarm.Title,
            Alarmiert = string.Empty,
            IstEinsatz = true,
            AlarmierungsZeit = alarm.Date != DateTime.MinValue ? alarm.Date : now,
            EinsatzDatum = alarm.Date != DateTime.MinValue ? alarm.Date : now,
            ExportPfad = string.IsNullOrWhiteSpace(alarm.Text)
                ? alarm.Title
                : $"{alarm.Title}: {alarm.Text}",
            Einsatzleiter = string.Empty,
            Fuehrungsassistent = string.Empty,
        };

        _logger.LogInformation("Divera-Import: Alarm {AlarmId} '{Title}' wird als Einsatz importiert", alarm.Id, alarm.Title);

        await _einsatzService.StartEinsatzAsync(einsatzData);

        return Ok(new { success = true, einsatzNummer = einsatzData.EinsatzNummer });
    }
}

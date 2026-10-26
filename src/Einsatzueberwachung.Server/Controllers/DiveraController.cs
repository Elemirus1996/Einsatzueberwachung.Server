using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Divera;
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

    /// <summary>
    /// Foto der Einsatzleitung (EL-Ansicht, Knopf „Foto“) in den Messenger-Chat des Divera-Einsatzes senden.
    /// Das Bild wird im Browser verkleinert; hier nur Groesse und Bildformat pruefen und weiterreichen.
    /// </summary>
    [HttpPost("foto")]
    [RequestSizeLimit(DiveraFotoLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = DiveraFotoLimit)]
    public async Task<IActionResult> SendFoto(IFormFile? foto, [FromForm] string? text)
    {
        await _diveraService.LadeEinstellungenAsync();
        if (!_diveraService.FotosSenden)
            return Conflict(new ErrorResponse("Fotos an Divera sind in den Einstellungen nicht eingeschaltet."));

        var einsatz = _einsatzService.CurrentEinsatz;
        if (string.IsNullOrWhiteSpace(einsatz.Einsatzort) || einsatz.EinsatzEnde is not null)
            return Conflict(new ErrorResponse("Kein laufender Einsatz."));

        if (foto is null || foto.Length == 0 || foto.Length > DiveraService.MaxFotoBytes)
            return BadRequest(new ErrorResponse("Foto fehlt oder ist größer als 10 MB."));
        if (text is { Length: > 500 })
            return BadRequest(new ErrorResponse("Text ist zu lang (höchstens 500 Zeichen)."));

        byte[] bild;
        using (var stream = new MemoryStream())
        {
            await foto.CopyToAsync(stream);
            bild = stream.ToArray();
        }

        var contentType = BildFormat(bild);
        if (contentType is null)
            return BadRequest(new ErrorResponse("Nur JPEG-, PNG- oder WebP-Fotos."));

        var alarmId = EinsatzAlarmId(einsatz.DiveraAlarmId, await _diveraService.GetActiveAlarmsAsync());
        if (alarmId is null)
            return Conflict(new ErrorResponse("Der Einsatz ist mit keinem Divera-Alarm verknüpft. Einsatz über „Alarm aus Divera übernehmen“ starten."));

        var dateiname = $"EL-{_timeService.Now:yyyyMMdd-HHmmss}{(contentType == "image/png" ? ".png" : contentType == "image/webp" ? ".webp" : ".jpg")}";
        var ergebnis = await _diveraService.SendeFotoAnEinsatzChatAsync(alarmId.Value, bild, dateiname, contentType, text);
        _logger.LogInformation("Divera-Foto fuer Alarm {AlarmId}: {Erfolg}", alarmId, ergebnis.Erfolg);
        return ergebnis.Erfolg ? Ok(new { message = ergebnis.Meldung }) : StatusCode(StatusCodes.Status502BadGateway, new ErrorResponse(ergebnis.Meldung));
    }

    private const long DiveraFotoLimit = 12 * 1024 * 1024;

    /// <summary>Alarm des Einsatzes; ohne Verknuepfung nur dann, wenn genau ein Divera-Alarm offen ist.</summary>
    public static int? EinsatzAlarmId(int? verknuepft, IReadOnlyList<DiveraAlarm> offeneAlarme) =>
        verknuepft ?? (offeneAlarme.Count == 1 ? offeneAlarme[0].Id : null);

    /// <summary>Bildformat an den ersten Bytes erkennen (nicht am Dateinamen/Content-Type des Browsers).</summary>
    public static string? BildFormat(byte[] b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
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
            DiveraAlarmId = alarm.Id,
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

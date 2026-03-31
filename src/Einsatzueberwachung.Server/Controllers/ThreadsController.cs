using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ThreadsController : ControllerBase
{
    private readonly IEinsatzService _einsatzService;
    private readonly ILogger<ThreadsController> _logger;

    public ThreadsController(IEinsatzService einsatzService, ILogger<ThreadsController> logger)
    {
        _einsatzService = einsatzService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetNotes([FromQuery] string filter = "alle")
    {
        try
        {
            IEnumerable<GlobalNotesEntry> query = _einsatzService.GlobalNotes;
            var normalized = filter.Trim().ToLowerInvariant();

            if (normalized == "funk")
            {
                query = query.Where(n => string.Equals(n.SourceType, "Funk", StringComparison.OrdinalIgnoreCase));
            }
            else if (normalized == "notiz")
            {
                query = query.Where(n => !string.Equals(n.SourceType, "Funk", StringComparison.OrdinalIgnoreCase));
            }

            var notes = query
                .OrderByDescending(n => n.Timestamp)
                .Take(200)
                .Select(ToNoteDto)
                .ToList();

            return Ok(new { notes, count = notes.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Threads");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateNote([FromBody] CreateNoteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "Text darf nicht leer sein." });
        }

        try
        {
            var sourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "Notiz" : request.SourceType.Trim();
            var note = await _einsatzService.AddGlobalNoteWithSourceAsync(
                request.Text.Trim(),
                string.IsNullOrWhiteSpace(request.SourceTeamId) ? "mobile" : request.SourceTeamId.Trim(),
                string.IsNullOrWhiteSpace(request.SourceTeamName) ? "Mobile" : request.SourceTeamName.Trim(),
                sourceType,
                ParseNoteType(sourceType),
                string.IsNullOrWhiteSpace(request.CreatedBy) ? "Mobile" : request.CreatedBy.Trim());

            return CreatedAtAction(nameof(GetNotes), new { filter = "alle" }, ToNoteDto(note));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Erstellen einer Notiz");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("{noteId}/replies")]
    public async Task<IActionResult> AddReply(string noteId, [FromBody] CreateReplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "Antworttext darf nicht leer sein." });
        }

        try
        {
            var reply = await _einsatzService.AddReplyToNoteAsync(
                noteId,
                request.Text.Trim(),
                string.IsNullOrWhiteSpace(request.SourceTeamId) ? "mobile" : request.SourceTeamId.Trim(),
                string.IsNullOrWhiteSpace(request.SourceTeamName) ? "Mobile" : request.SourceTeamName.Trim(),
                string.IsNullOrWhiteSpace(request.CreatedBy) ? "Mobile" : request.CreatedBy.Trim());

            return Ok(new { message = "Antwort hinzugefuegt", reply = ToReplyDto(reply) });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Erstellen einer Antwort fuer {NoteId}", noteId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static GlobalNotesEntryType ParseNoteType(string sourceType)
    {
        return string.Equals(sourceType, "Funk", StringComparison.OrdinalIgnoreCase)
            ? GlobalNotesEntryType.EinsatzUpdate
            : GlobalNotesEntryType.Manual;
    }

    private static NoteDto ToNoteDto(GlobalNotesEntry note)
    {
        var replies = note.Replies
            .OrderBy(r => r.Timestamp)
            .Select(ToReplyDto)
            .ToList();

        return new NoteDto(
            note.Id,
            note.Timestamp,
            note.Text,
            note.SourceTeamId,
            note.SourceTeamName,
            note.SourceType,
            note.CreatedBy,
            note.IsEdited,
            replies,
            note.FormattedTimestamp,
            note.FormattedDate);
    }

    private static ReplyDto ToReplyDto(GlobalNotesReply reply)
    {
        return new ReplyDto(
            reply.Id,
            reply.NoteId,
            reply.Timestamp,
            reply.Text,
            reply.SourceTeamId,
            reply.SourceTeamName,
            reply.CreatedBy,
            reply.FormattedTimestamp);
    }

    public sealed record CreateNoteRequest(
        string Text,
        string? SourceType,
        string? SourceTeamId,
        string? SourceTeamName,
        string? CreatedBy);

    public sealed record CreateReplyRequest(
        string Text,
        string? SourceTeamId,
        string? SourceTeamName,
        string? CreatedBy);

    public sealed record NoteDto(
        string Id,
        DateTime Timestamp,
        string Text,
        string SourceTeamId,
        string SourceTeamName,
        string SourceType,
        string CreatedBy,
        bool IsEdited,
        IReadOnlyList<ReplyDto> Replies,
        string FormattedTimestamp,
        string FormattedDate);

    public sealed record ReplyDto(
        string Id,
        string NoteId,
        DateTime Timestamp,
        string Text,
        string SourceTeamId,
        string SourceTeamName,
        string CreatedBy,
        string FormattedTimestamp);
}

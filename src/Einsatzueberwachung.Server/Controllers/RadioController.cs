using Einsatzueberwachung.Server.Services.Radio;
using Microsoft.AspNetCore.Mvc;

namespace Einsatzueberwachung.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class RadioController : ControllerBase
{
    private readonly IRadioService _radioService;

    public RadioController(IRadioService radioService)
    {
        _radioService = radioService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMessages(CancellationToken cancellationToken)
    {
        var messages = await _radioService.GetMessagesAsync(cancellationToken);
        return Ok(new { messages, count = messages.Count });
    }

    [HttpPost]
    public async Task<IActionResult> AddMessage([FromBody] CreateRadioMessageBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Text))
        {
            return BadRequest(new { error = "Text darf nicht leer sein." });
        }

        var request = new CreateRadioMessageRequest(
            body.Text.Trim(),
            string.IsNullOrWhiteSpace(body.SourceTeamId) ? "einsatzleitung" : body.SourceTeamId.Trim(),
            string.IsNullOrWhiteSpace(body.SourceTeamName) ? "Einsatzleitung" : body.SourceTeamName.Trim(),
            string.IsNullOrWhiteSpace(body.CreatedBy) ? "Einsatzleitung" : body.CreatedBy.Trim());

        var message = await _radioService.AddMessageAsync(request, cancellationToken);
        return Ok(new { message });
    }

    [HttpPost("{messageId}/replies")]
    public async Task<IActionResult> AddReply(string messageId, [FromBody] CreateRadioReplyBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Text))
        {
            return BadRequest(new { error = "Antworttext darf nicht leer sein." });
        }

        try
        {
            var request = new CreateRadioReplyRequest(
                body.Text.Trim(),
                string.IsNullOrWhiteSpace(body.SourceTeamId) ? "einsatzleitung" : body.SourceTeamId.Trim(),
                string.IsNullOrWhiteSpace(body.SourceTeamName) ? "Einsatzleitung" : body.SourceTeamName.Trim(),
                string.IsNullOrWhiteSpace(body.CreatedBy) ? "Einsatzleitung" : body.CreatedBy.Trim());

            var reply = await _radioService.AddReplyAsync(messageId, request, cancellationToken);
            return Ok(new { reply });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    public sealed record CreateRadioMessageBody(string Text, string? SourceTeamId, string? SourceTeamName, string? CreatedBy);
    public sealed record CreateRadioReplyBody(string Text, string? SourceTeamId, string? SourceTeamName, string? CreatedBy);
}

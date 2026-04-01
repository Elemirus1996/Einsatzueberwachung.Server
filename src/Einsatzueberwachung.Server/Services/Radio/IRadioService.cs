namespace Einsatzueberwachung.Server.Services.Radio;

public interface IRadioService
{
    Task<IReadOnlyList<RadioMessageDto>> GetMessagesAsync(CancellationToken cancellationToken = default);
    Task<RadioMessageDto> AddMessageAsync(CreateRadioMessageRequest request, CancellationToken cancellationToken = default);
    Task<RadioReplyDto> AddReplyAsync(string messageId, CreateRadioReplyRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreateRadioMessageRequest(
    string Text,
    string SourceTeamId,
    string SourceTeamName,
    string CreatedBy);

public sealed record CreateRadioReplyRequest(
    string Text,
    string SourceTeamId,
    string SourceTeamName,
    string CreatedBy);

public sealed record RadioMessageDto(
    string Id,
    DateTime Timestamp,
    string Text,
    string SourceTeamId,
    string SourceTeamName,
    string CreatedBy,
    IReadOnlyList<RadioReplyDto> Replies,
    string FormattedTimestamp);

public sealed record RadioReplyDto(
    string Id,
    string MessageId,
    DateTime Timestamp,
    string Text,
    string SourceTeamId,
    string SourceTeamName,
    string CreatedBy,
    string FormattedTimestamp);

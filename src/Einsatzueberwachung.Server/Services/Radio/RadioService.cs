using Einsatzueberwachung.Server.Data;
using Einsatzueberwachung.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Einsatzueberwachung.Server.Services.Radio;

public sealed class RadioService : IRadioService
{
    private readonly IDbContextFactory<RuntimeDbContext> _dbContextFactory;
    private readonly IHubContext<EinsatzHub> _hubContext;

    public RadioService(IDbContextFactory<RuntimeDbContext> dbContextFactory, IHubContext<EinsatzHub> hubContext)
    {
        _dbContextFactory = dbContextFactory;
        _hubContext = hubContext;
    }

    public async Task<IReadOnlyList<RadioMessageDto>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var messages = await db.RadioMessages
            .AsNoTracking()
            .Include(x => x.Replies)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(300)
            .ToListAsync(cancellationToken);

        return messages.Select(ToDto).ToList();
    }

    public async Task<RadioMessageDto> AddMessageAsync(CreateRadioMessageRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new RadioMessageEntity
        {
            Id = Guid.NewGuid().ToString(),
            TimestampUtc = DateTime.UtcNow,
            Text = request.Text,
            SourceTeamId = request.SourceTeamId,
            SourceTeamName = request.SourceTeamName,
            CreatedBy = request.CreatedBy
        };

        db.RadioMessages.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        var dto = ToDto(entity);
        await PublishAsync("radio.added", dto);
        return dto;
    }

    public async Task<RadioReplyDto> AddReplyAsync(string messageId, CreateRadioReplyRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var message = await db.RadioMessages
            .Include(x => x.Replies)
            .FirstOrDefaultAsync(x => x.Id == messageId, cancellationToken);

        if (message is null)
        {
            throw new InvalidOperationException($"Funkspruch mit ID {messageId} nicht gefunden");
        }

        var reply = new RadioReplyEntity
        {
            Id = Guid.NewGuid().ToString(),
            MessageId = messageId,
            TimestampUtc = DateTime.UtcNow,
            Text = request.Text,
            SourceTeamId = request.SourceTeamId,
            SourceTeamName = request.SourceTeamName,
            CreatedBy = request.CreatedBy
        };

        message.Replies.Add(reply);
        await db.SaveChangesAsync(cancellationToken);

        var dto = ToReplyDto(reply);
        await PublishAsync("radio.reply.added", new { messageId, reply = dto });
        return dto;
    }

    private async Task PublishAsync(string eventName, object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        await _hubContext.Clients.All.SendAsync("einsatz:update", eventName, json);
    }

    private static RadioMessageDto ToDto(RadioMessageEntity entity)
    {
        var timestamp = DateTime.SpecifyKind(entity.TimestampUtc, DateTimeKind.Utc).ToLocalTime();
        var replies = entity.Replies
            .OrderBy(x => x.TimestampUtc)
            .Select(ToReplyDto)
            .ToList();

        return new RadioMessageDto(
            entity.Id,
            timestamp,
            entity.Text,
            entity.SourceTeamId,
            entity.SourceTeamName,
            entity.CreatedBy,
            replies,
            timestamp.ToString("HH:mm:ss"));
    }

    private static RadioReplyDto ToReplyDto(RadioReplyEntity entity)
    {
        var timestamp = DateTime.SpecifyKind(entity.TimestampUtc, DateTimeKind.Utc).ToLocalTime();

        return new RadioReplyDto(
            entity.Id,
            entity.MessageId,
            timestamp,
            entity.Text,
            entity.SourceTeamId,
            entity.SourceTeamName,
            entity.CreatedBy,
            timestamp.ToString("HH:mm:ss"));
    }
}

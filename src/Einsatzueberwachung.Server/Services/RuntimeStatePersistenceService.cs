using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Einsatzueberwachung.Server.Services;

public sealed class RuntimeStatePersistenceService : BackgroundService
{
    private const int RuntimeStateRowId = 1;

    private readonly IDbContextFactory<RuntimeDbContext> _dbContextFactory;
    private readonly IEinsatzService _einsatzService;
    private readonly ILogger<RuntimeStatePersistenceService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private volatile bool _isDirty;

    public RuntimeStatePersistenceService(
        IDbContextFactory<RuntimeDbContext> dbContextFactory,
        IEinsatzService einsatzService,
        ILogger<RuntimeStatePersistenceService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _einsatzService = einsatzService;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        _jsonOptions.Converters.Add(new CoordinateTupleConverter());
        _jsonOptions.Converters.Add(new NullableCoordinateTupleConverter());
        _jsonOptions.Converters.Add(new CoordinateTupleListConverter());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureDatabaseAsync(stoppingToken);
        await RestoreRuntimeStateAsync(stoppingToken);

        Subscribe();

        var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (_isDirty)
                {
                    _isDirty = false;
                    await PersistRuntimeStateAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            timer.Dispose();
            Unsubscribe();

            if (_isDirty)
            {
                try
                {
                    await PersistRuntimeStateAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler beim finalen Persistieren des Runtime-Status");
                }
            }
        }
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    private async Task RestoreRuntimeStateAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var state = await db.RuntimeStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == RuntimeStateRowId, cancellationToken);

        if (state is null || string.IsNullOrWhiteSpace(state.JsonPayload))
        {
            return;
        }

        var snapshot = JsonSerializer.Deserialize<EinsatzRuntimeSnapshot>(state.JsonPayload, _jsonOptions);
        if (snapshot is null)
        {
            return;
        }

        await _einsatzService.ImportRuntimeSnapshotAsync(snapshot);
        await MigrateLegacyFunkToRadioAsync(db, cancellationToken);
        _logger.LogInformation("Runtime-Status aus SQLite wiederhergestellt ({UpdatedAtUtc})", state.UpdatedAtUtc);
    }

    private async Task MigrateLegacyFunkToRadioAsync(RuntimeDbContext db, CancellationToken cancellationToken)
    {
        var legacyFunkNotes = _einsatzService.GlobalNotes
            .Where(n => string.Equals(n.SourceType, "Funk", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (legacyFunkNotes.Count == 0)
        {
            return;
        }

        var existingIds = await db.RadioMessages
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var existingIdSet = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var created = 0;

        foreach (var note in legacyFunkNotes)
        {
            if (string.IsNullOrWhiteSpace(note.Id) || existingIdSet.Contains(note.Id))
            {
                continue;
            }

            var message = new RadioMessageEntity
            {
                Id = note.Id,
                TimestampUtc = EnsureUtc(note.Timestamp),
                Text = note.Text,
                SourceTeamId = string.IsNullOrWhiteSpace(note.SourceTeamId) ? "einsatzleitung" : note.SourceTeamId,
                SourceTeamName = string.IsNullOrWhiteSpace(note.SourceTeamName) ? "Einsatzleitung" : note.SourceTeamName,
                CreatedBy = string.IsNullOrWhiteSpace(note.CreatedBy) ? "System" : note.CreatedBy,
                Replies = note.Replies.Select(reply => new RadioReplyEntity
                {
                    Id = string.IsNullOrWhiteSpace(reply.Id) ? Guid.NewGuid().ToString() : reply.Id,
                    MessageId = note.Id,
                    TimestampUtc = EnsureUtc(reply.Timestamp),
                    Text = reply.Text,
                    SourceTeamId = string.IsNullOrWhiteSpace(reply.SourceTeamId) ? "einsatzleitung" : reply.SourceTeamId,
                    SourceTeamName = string.IsNullOrWhiteSpace(reply.SourceTeamName) ? "Einsatzleitung" : reply.SourceTeamName,
                    CreatedBy = string.IsNullOrWhiteSpace(reply.CreatedBy) ? "System" : reply.CreatedBy
                }).ToList()
            };

            db.RadioMessages.Add(message);
            existingIdSet.Add(message.Id);
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("{Count} alte Funkeintraege in die Radio-Tabelle migriert", created);
        }
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        if (value.Kind == DateTimeKind.Unspecified)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
        }

        return value.ToUniversalTime();
    }

    private async Task PersistRuntimeStateAsync(CancellationToken cancellationToken)
    {
        var snapshot = _einsatzService.ExportRuntimeSnapshot();
        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.RuntimeStates.FirstOrDefaultAsync(x => x.Id == RuntimeStateRowId, cancellationToken);

        if (row is null)
        {
            row = new RuntimeStateEntity
            {
                Id = RuntimeStateRowId,
                JsonPayload = json,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.RuntimeStates.Add(row);
        }
        else
        {
            row.JsonPayload = json;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void Subscribe()
    {
        _einsatzService.EinsatzChanged += OnDirty;
        _einsatzService.TeamAdded += OnTeamDirty;
        _einsatzService.TeamRemoved += OnTeamDirty;
        _einsatzService.TeamUpdated += OnTeamDirty;
        _einsatzService.NoteAdded += OnNoteDirty;
    }

    private void Unsubscribe()
    {
        _einsatzService.EinsatzChanged -= OnDirty;
        _einsatzService.TeamAdded -= OnTeamDirty;
        _einsatzService.TeamRemoved -= OnTeamDirty;
        _einsatzService.TeamUpdated -= OnTeamDirty;
        _einsatzService.NoteAdded -= OnNoteDirty;
    }

    private void OnDirty() => _isDirty = true;
    private void OnTeamDirty(Einsatzueberwachung.Domain.Models.Team _) => _isDirty = true;
    private void OnNoteDirty(Einsatzueberwachung.Domain.Models.GlobalNotesEntry _) => _isDirty = true;
}

using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Data;
using Microsoft.EntityFrameworkCore;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

public sealed class RuntimeStatePersistenceService : BackgroundService
{
    private const int RuntimeStateRowId = 1;

    private readonly IDbContextFactory<RuntimeDbContext> _dbContextFactory;
    private readonly IEinsatzService _einsatzService;
    private readonly ICollarTrackingService _collarTrackingService;
    private readonly ILogger<RuntimeStatePersistenceService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    // Verlaeufe laufender Suchen ohne Obergrenze, Punkt fuer Punkt (#89)
    private readonly TrackPunktSpeicher<CollarLocation> _halsbandPunkte;
    private readonly TrackPunktSpeicher<TeamPhoneLocation> _handyPunkte;

    private volatile bool _isDirty;

    public RuntimeStatePersistenceService(
        IDbContextFactory<RuntimeDbContext> dbContextFactory,
        IEinsatzService einsatzService,
        ICollarTrackingService collarTrackingService,
        ILogger<RuntimeStatePersistenceService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _einsatzService = einsatzService;
        _collarTrackingService = collarTrackingService;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        _jsonOptions.Converters.Add(new CoordinateTupleConverter());
        _jsonOptions.Converters.Add(new NullableCoordinateTupleConverter());
        _jsonOptions.Converters.Add(new CoordinateTupleListConverter());
        _halsbandPunkte = new("collar", p => p.Timestamp, p => (p.Latitude, p.Longitude), _jsonOptions);
        _handyPunkte = new("phone", p => p.Timestamp, p => (p.Latitude, p.Longitude), _jsonOptions);
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
        await TrackPunktSpeicher<CollarLocation>.TabelleAnlegenAsync(db, cancellationToken);
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
        await RestoreCollarAssignmentsAsync();

        // Verlaeufe aus der Punkt-Tabelle; Datenbestaende vor 1.11.7 stehen noch (gekuerzt) im Schnappschuss
        var halsband = await _halsbandPunkte.LadenAsync(db, cancellationToken);
        var handy = await _handyPunkte.LadenAsync(db, cancellationToken);
        if (halsband.Count > 0 || handy.Count > 0)
        {
            snapshot.CollarLocationHistory = halsband;
            snapshot.PhoneTrackHistory = handy;
        }
        else if (snapshot.CollarLocationHistory.Count > 0 || snapshot.PhoneTrackHistory.Count > 0)
        {
            _isDirty = true; // beim naechsten Takt in die Punkt-Tabelle uebernehmen
        }

        RestoreCollarLocationHistory(snapshot);
        RestorePhoneTrackHistory(snapshot);
        await MigrateLegacyFunkToRadioAsync(db, cancellationToken);
        _logger.LogInformation("Runtime-Status aus SQLite wiederhergestellt ({UpdatedAtUtc})", state.UpdatedAtUtc);
    }

    private async Task RestoreCollarAssignmentsAsync()
    {
        foreach (var team in _einsatzService.Teams.Where(t => !string.IsNullOrWhiteSpace(t.CollarId)))
        {
            try
            {
                await _collarTrackingService.AssignCollarToTeamAsync(team.CollarId!, team.TeamId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Konnte Halsband-Zuordnung nach Restore nicht wiederherstellen (Team: {TeamId}, Collar: {CollarId})",
                    team.TeamId,
                    team.CollarId);
            }
        }
    }

    private void RestoreCollarLocationHistory(EinsatzRuntimeSnapshot snapshot)
    {
        if (snapshot.CollarLocationHistory == null || snapshot.CollarLocationHistory.Count == 0)
            return;

        foreach (var kvp in snapshot.CollarLocationHistory)
        {
            if (kvp.Value is { Count: > 0 })
            {
                try { _collarTrackingService.SetLocationHistory(kvp.Key, kvp.Value); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Konnte Halsband-Positionsverlauf nicht wiederherstellen (Collar: {CollarId})", kvp.Key);
                }
            }
        }

        _logger.LogInformation("Halsband-Positionsverlauf für {Count} Halsband(e) wiederhergestellt",
            snapshot.CollarLocationHistory.Count);
    }

    private void RestorePhoneTrackHistory(EinsatzRuntimeSnapshot snapshot)
    {
        if (snapshot.PhoneTrackHistory == null || snapshot.PhoneTrackHistory.Count == 0)
            return;

        foreach (var kvp in snapshot.PhoneTrackHistory)
        {
            if (kvp.Value is { Count: > 0 })
            {
                try { _einsatzService.SetPhoneTrackHistory(kvp.Key, kvp.Value); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Konnte Telefon-GPS-Verlauf nicht wiederherstellen (Team: {TeamId})", kvp.Key);
                }
            }
        }

        _logger.LogInformation("Telefon-GPS-Verlauf für {Count} Team(s) wiederhergestellt",
            snapshot.PhoneTrackHistory.Count);
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
            return Ortszeit.NachUtc(value);
        }

        return value.ToUniversalTime();
    }

    private async Task PersistRuntimeStateAsync(CancellationToken cancellationToken)
    {
        var snapshot = _einsatzService.ExportRuntimeSnapshot();

        // Verlaeufe laufender Suchen liegen nicht im Schnappschuss, sondern in runtime_track_points
        snapshot.CollarLocationHistory.Clear();
        snapshot.PhoneTrackHistory.Clear();

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

        // Halsbaender: auch solche, die (noch) nicht in Collars stehen, aber Verlauf haben – sonst wuerden ihre
        // Punkte nach einer Wiederherstellung geloescht
        var halsbandIds = _collarTrackingService.Collars.Select(c => c.Id).Union(_halsbandPunkte.GespeicherteSchluessel).ToList();
        var halsbandVerlaeufe = halsbandIds.ToDictionary(id => id, id => _collarTrackingService.GetLocationHistory(id));
        await _halsbandPunkte.AbgleichenAsync(db, halsbandVerlaeufe, cancellationToken);
        await _handyPunkte.AbgleichenAsync(db, _einsatzService.GetAllPhoneTrackHistories(), cancellationToken);
    }

    // WICHTIG: Wird ein neues Event zu IEinsatzService hinzugefügt, das persistenten Zustand verändert,
    // muss es hier in Subscribe() UND Unsubscribe() eingetragen werden — sonst gehen Daten bei einem
    // Server-Neustart verloren (der Dirty-Flag wird nie gesetzt und der Timer schreibt nichts in SQLite).
    private void Subscribe()
    {
        _einsatzService.EinsatzChanged += OnDirty;
        _einsatzService.TeamAdded += OnTeamDirty;
        _einsatzService.TeamRemoved += OnTeamDirty;
        _einsatzService.TeamUpdated += OnTeamDirty;
        _einsatzService.NoteAdded += OnNoteDirty;
        _einsatzService.TrackSnapshotAdded += OnTrackDirty;
        _einsatzService.CompletedSearchUpdated += OnCompletedSearchDirty;
        // Live-Verläufe: Halsband-GPS und Telefon-GPS während laufender Suche persistieren
        _collarTrackingService.CollarLocationReceived += OnCollarLocationDirty;
        _einsatzService.TeamPhoneTrackPointAdded += OnPhoneTrackPointDirty;
        _einsatzService.TeamPhoneLocationRemoved += OnPhoneRemovedDirty;
    }

    private void Unsubscribe()
    {
        _einsatzService.EinsatzChanged -= OnDirty;
        _einsatzService.TeamAdded -= OnTeamDirty;
        _einsatzService.TeamRemoved -= OnTeamDirty;
        _einsatzService.TeamUpdated -= OnTeamDirty;
        _einsatzService.NoteAdded -= OnNoteDirty;
        _einsatzService.TrackSnapshotAdded -= OnTrackDirty;
        _einsatzService.CompletedSearchUpdated -= OnCompletedSearchDirty;
        _collarTrackingService.CollarLocationReceived -= OnCollarLocationDirty;
        _einsatzService.TeamPhoneTrackPointAdded -= OnPhoneTrackPointDirty;
        _einsatzService.TeamPhoneLocationRemoved -= OnPhoneRemovedDirty;
    }

    private void OnDirty() => _isDirty = true;
    private void OnTeamDirty(Einsatzueberwachung.Domain.Models.Team _) => _isDirty = true;
    private void OnNoteDirty(Einsatzueberwachung.Domain.Models.GlobalNotesEntry _) => _isDirty = true;
    private void OnTrackDirty(Einsatzueberwachung.Domain.Models.TeamTrackSnapshot _) => _isDirty = true;
    private void OnCompletedSearchDirty(Einsatzueberwachung.Domain.Models.CompletedSearch _) => _isDirty = true;
    private void OnCollarLocationDirty(string collarId, Einsatzueberwachung.Domain.Models.CollarLocation location) => _isDirty = true;
    private void OnPhoneRemovedDirty(string teamId) => _isDirty = true;
    private void OnPhoneTrackPointDirty(string teamId, string memberId, Einsatzueberwachung.Domain.Models.TeamPhoneLocation location) => _isDirty = true;
}

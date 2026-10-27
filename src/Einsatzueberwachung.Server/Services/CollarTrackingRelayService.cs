// Relay-Service: Leitet CollarTrackingService-Events an SignalR-Clients weiter
// Broadcasts GPS-Positionen und erzeugt Collar-spezifische Warnungen in Echtzeit

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Server.Services;

public sealed class CollarTrackingRelayService : IHostedService
{
    private const int MonitorTickSeconds = 5;

    private readonly ICollarTrackingService _trackingService;
    private readonly IEinsatzService _einsatzService;
    private readonly ISettingsService _settingsService;
    private readonly IWarningService _warningService;
    private readonly ITimeService _timeService;
    private readonly IHubContext<EinsatzHub> _hubContext;
    private readonly ILogger<CollarTrackingRelayService> _logger;

    private readonly Dictionary<string, DateTime> _lastSignalByCollar = new();
    private readonly Dictionary<string, DateTime> _lastWarningByKey = new();
    private readonly object _warningLock = new();

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;

    public CollarTrackingRelayService(
        ICollarTrackingService trackingService,
        IEinsatzService einsatzService,
        ISettingsService settingsService,
        IWarningService warningService,
        ITimeService timeService,
        IHubContext<EinsatzHub> hubContext,
        ILogger<CollarTrackingRelayService> logger)
    {
        _trackingService = trackingService;
        _einsatzService = einsatzService;
        _settingsService = settingsService;
        _warningService = warningService;
        _timeService = timeService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _trackingService.CollarLocationReceived += OnCollarLocationReceived;
        _trackingService.OutOfBoundsDetected += OnOutOfBoundsDetected;
        SeedLastSignalTimestamps();

        _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = MonitorNoSignalAsync(_monitorCts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _trackingService.CollarLocationReceived -= OnCollarLocationReceived;
        _trackingService.OutOfBoundsDetected -= OnOutOfBoundsDetected;

        if (_monitorCts is not null)
        {
            _monitorCts.Cancel();
        }

        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask;
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
        }

        _monitorCts?.Dispose();
        _monitorCts = null;
        _monitorTask = null;
    }

    private void OnCollarLocationReceived(string collarId, CollarLocation location)
    {
        var now = _timeService.Now;

        lock (_warningLock)
        {
            _lastSignalByCollar[collarId] = now;
        }

        TryEmitLowBatteryWarning(collarId, location, now);

        _ = PublishAsync("collar.location", new
        {
            collarId,
            latitude = location.Latitude,
            longitude = location.Longitude,
            timestamp = location.Timestamp,
            batteryLevel = location.BatteryLevel
        });
    }

    private void OnOutOfBoundsDetected(string teamId, string collarId, CollarLocation location)
    {
        var now = _timeService.Now;
        if (!ShouldEmitWarning(WarningRuleDefinition.Sources.CollarOutOfBounds, $"{teamId}:{collarId}", now))
        {
            return;
        }

        _logger.LogWarning(
            "Hund mit Halsband {CollarId} hat Suchgebiet von Team {TeamId} verlassen! Position: {Lat}, {Lng}",
            collarId, teamId, location.Latitude, location.Longitude);

        var collar = _trackingService.Collars.FirstOrDefault(c => c.Id == collarId);
        var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == teamId);
        var collarLabel = collar?.CollarName ?? collarId;
        var teamLabel = team?.TeamName ?? teamId;

        _warningService.AddWarning(new WarningEntry
        {
            Title = "Hund hat Suchgebiet verlassen",
            Message = $"Halsband \"{collarLabel}\" (Team: {teamLabel}) hat das zugewiesene Suchgebiet verlassen.",
            Level = WarningLevel.Critical,
            TeamId = teamId,
            NavigationUrl = $"/einsatz-karte?focusCollarId={Uri.EscapeDataString(collarId)}",
            Source = WarningRuleDefinition.Sources.CollarOutOfBounds,
            Timestamp = now
        });

        _ = PublishAsync("collar.outofbounds", new
        {
            teamId,
            collarId,
            latitude = location.Latitude,
            longitude = location.Longitude,
            timestamp = location.Timestamp
        });
    }

    private void TryEmitLowBatteryWarning(string collarId, CollarLocation location, DateTime now)
    {
        if (location.BatteryLevel != 1)
        {
            return;
        }

        if (!TryGetAssignedRunningTeam(collarId, out var collar, out var team))
        {
            return;
        }

        if (!ShouldEmitWarning(WarningRuleDefinition.Sources.CollarLowBattery, $"{team.TeamId}:{collarId}", now))
        {
            return;
        }

        var collarLabel = collar.CollarName;
        var teamLabel = team.TeamName;

        _warningService.AddWarning(new WarningEntry
        {
            Title = "Halsband-Akku niedrig",
            Message = $"Halsband \"{collarLabel}\" (Team: {teamLabel}) meldet niedrigen Akkustand (1/3).",
            Level = WarningLevel.Warning,
            TeamId = team.TeamId,
            NavigationUrl = $"/einsatz-karte?focusCollarId={Uri.EscapeDataString(collarId)}",
            Source = WarningRuleDefinition.Sources.CollarLowBattery,
            Timestamp = now
        });
    }

    private async Task MonitorNoSignalAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(MonitorTickSeconds));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                CheckNoSignalWarnings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler bei der Kein-Signal-Ueberwachung der GPS-Halsbaender.");
            }
        }
    }

    private void CheckNoSignalWarnings()
    {
        int timeoutSeconds;
        try
        {
            timeoutSeconds = Math.Max(1, _settingsService.GetAppSettingsAsync().GetAwaiter().GetResult().CollarNoSignalTimeoutSeconds);
        }
        catch
        {
            timeoutSeconds = 30;
        }

        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var now = _timeService.Now;

        foreach (var collar in _trackingService.Collars.Where(c => c.IsAssigned && !string.IsNullOrWhiteSpace(c.AssignedTeamId)))
        {
            var team = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == collar.AssignedTeamId);
            if (team is null || !team.IsRunning)
            {
                continue;
            }

            var lastSignal = team.StartTime;
            lock (_warningLock)
            {
                if (_lastSignalByCollar.TryGetValue(collar.Id, out var seenAt) && seenAt > lastSignal)
                {
                    lastSignal = seenAt;
                }
                else
                {
                    var restoredLatest = _trackingService.GetLatestLocation(collar.Id);
                    if (restoredLatest != null)
                    {
                        var restoredSeenAt = NormalizeSeenTimestamp(restoredLatest.Timestamp, now, collar.Id);
                        if (restoredSeenAt > lastSignal)
                        {
                            lastSignal = restoredSeenAt;
                            _lastSignalByCollar[collar.Id] = restoredSeenAt;
                        }
                    }
                }
            }

            if (now - lastSignal < timeout)
            {
                continue;
            }

            if (!ShouldEmitWarning(WarningRuleDefinition.Sources.CollarNoSignal, $"{team.TeamId}:{collar.Id}", now))
            {
                continue;
            }

            var collarLabel = collar.CollarName;
            var teamLabel = team.TeamName;

            _warningService.AddWarning(new WarningEntry
            {
                Title = "Kein Signal vom Halsband",
                Message = $"Halsband \"{collarLabel}\" (Team: {teamLabel}) hat seit {timeoutSeconds} Sekunden kein Signal gesendet.",
                Level = WarningLevel.Critical,
                TeamId = team.TeamId,
                NavigationUrl = $"/einsatz-karte?focusCollarId={Uri.EscapeDataString(collar.Id)}",
                Source = WarningRuleDefinition.Sources.CollarNoSignal,
                Timestamp = now
            });
        }
    }

    private bool TryGetAssignedRunningTeam(string collarId, out Collar collar, out Team team)
    {
        var matchedCollar = _trackingService.Collars.FirstOrDefault(c => c.Id == collarId);
        if (matchedCollar is null || !matchedCollar.IsAssigned || string.IsNullOrWhiteSpace(matchedCollar.AssignedTeamId))
        {
            collar = new Collar();
            team = new Team();
            return false;
        }

        var assignedTeamId = matchedCollar.AssignedTeamId;
        var matchedTeam = _einsatzService.Teams.FirstOrDefault(t => t.TeamId == assignedTeamId);
        if (matchedTeam is null || !matchedTeam.IsRunning)
        {
            collar = new Collar();
            team = new Team();
            return false;
        }

        collar = matchedCollar;
        team = matchedTeam;
        return true;
    }

    private void SeedLastSignalTimestamps()
    {
        var now = _timeService.Now;
        lock (_warningLock)
        {
            foreach (var collar in _trackingService.Collars)
            {
                var latest = _trackingService.GetLatestLocation(collar.Id);
                if (latest != null)
                {
                    _lastSignalByCollar[collar.Id] = NormalizeSeenTimestamp(latest.Timestamp, now, collar.Id);
                }
            }
        }
    }

    private DateTime NormalizeSeenTimestamp(DateTime timestamp, DateTime now, string collarId)
    {
        var seenAt = timestamp.Kind == DateTimeKind.Utc
            ? Ortszeit.AusUtc(timestamp)
            : timestamp;

        if (seenAt > now)
        {
            _logger.LogWarning(
                "Halsband {CollarId} lieferte Zeitstempel in der Zukunft ({SeenAt}), clamp auf {Now}.",
                collarId,
                seenAt,
                now);
            return now;
        }

        return seenAt;
    }

    private bool ShouldEmitWarning(string source, string scopeKey, DateTime now)
    {
        var rule = _warningService.GetRuleConfig(source);
        var cooldownSeconds = Math.Max(0, rule.CooldownSeconds);
        if (cooldownSeconds == 0)
        {
            return true;
        }

        var cooldown = TimeSpan.FromSeconds(cooldownSeconds);
        var key = $"{source}:{scopeKey}";

        lock (_warningLock)
        {
            if (_lastWarningByKey.TryGetValue(key, out var lastSentAt) && now - lastSentAt < cooldown)
            {
                return false;
            }

            _lastWarningByKey[key] = now;
            return true;
        }
    }

    private Task PublishAsync(string eventName, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return _hubContext.Clients.All.SendAsync("einsatz:update", eventName, json);
    }
}

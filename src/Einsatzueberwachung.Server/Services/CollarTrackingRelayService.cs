// Relay-Service: Leitet CollarTrackingService-Events an SignalR-Clients weiter
// Broadcasts GPS-Positionen und Out-of-Bounds-Warnungen in Echtzeit

using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Einsatzueberwachung.Server.Services;

public sealed class CollarTrackingRelayService : IHostedService
{
    private readonly ICollarTrackingService _trackingService;
    private readonly IHubContext<EinsatzHub> _hubContext;
    private readonly ILogger<CollarTrackingRelayService> _logger;

    public CollarTrackingRelayService(
        ICollarTrackingService trackingService,
        IHubContext<EinsatzHub> hubContext,
        ILogger<CollarTrackingRelayService> logger)
    {
        _trackingService = trackingService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _trackingService.CollarLocationReceived += OnCollarLocationReceived;
        _trackingService.OutOfBoundsDetected += OnOutOfBoundsDetected;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _trackingService.CollarLocationReceived -= OnCollarLocationReceived;
        _trackingService.OutOfBoundsDetected -= OnOutOfBoundsDetected;
        return Task.CompletedTask;
    }

    private void OnCollarLocationReceived(string collarId, CollarLocation location)
    {
        _ = PublishAsync("collar.location", new
        {
            collarId,
            latitude = location.Latitude,
            longitude = location.Longitude,
            timestamp = location.Timestamp
        });
    }

    private void OnOutOfBoundsDetected(string teamId, string collarId, CollarLocation location)
    {
        _logger.LogWarning(
            "Hund mit Halsband {CollarId} hat Suchgebiet von Team {TeamId} verlassen! Position: {Lat}, {Lng}",
            collarId, teamId, location.Latitude, location.Longitude);

        _ = PublishAsync("collar.outofbounds", new
        {
            teamId,
            collarId,
            latitude = location.Latitude,
            longitude = location.Longitude,
            timestamp = location.Timestamp
        });
    }

    private Task PublishAsync(string eventName, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return _hubContext.Clients.All.SendAsync("einsatz:update", eventName, json);
    }
}

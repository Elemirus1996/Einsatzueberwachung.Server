using Einsatzueberwachung.Domain.Interfaces;
using Microsoft.Extensions.Hosting;

namespace Einsatzueberwachung.Server.Services;

public sealed class TeamTimerTickService : BackgroundService
{
    private readonly IEinsatzService _einsatzService;
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(1));

    public TeamTimerTickService(IEinsatzService einsatzService)
    {
        _einsatzService = einsatzService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.Now;
            var runningTeams = _einsatzService.Teams
                .Where(t => t.IsRunning)
                .ToList();

            foreach (var team in runningTeams)
            {
                team.Tick(now);
                await _einsatzService.UpdateTeamAsync(team);
            }

            // Pausierende Teams ebenfalls broadcasten, damit der Pausen-Countdown live läuft
            var pausingTeams = _einsatzService.Teams
                .Where(t => t.IsPausing)
                .ToList();

            foreach (var team in pausingTeams)
            {
                await _einsatzService.UpdateTeamAsync(team);
            }
        }
    }

    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }
}
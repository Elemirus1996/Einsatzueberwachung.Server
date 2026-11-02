using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Components.Pages;

namespace Einsatzueberwachung.Tests;

public class EinsatzMonitorTeamOrderingTests
{
    [Fact]
    public void OrderTeamsForMonitor_UnrankedLegacyTeamsKeepStatusOrder()
    {
        var alpha = new Team { TeamName = "Alpha" };
        var bravo = new Team { TeamName = "Bravo" };
        var charlie = new Team { TeamName = "Charlie" };
        var startTime = DateTime.Now.AddMinutes(-5);
        var now = DateTime.Now;

        alpha.StartTimer(startTime);
        bravo.StartTimer(startTime);
        charlie.StartTimer(startTime);

        var orderBeforePause = EinsatzMonitor.OrderTeamsForMonitor([charlie, bravo, alpha]).ToArray();
        charlie.PauseTimer(now);
        Assert.Equal(orderBeforePause, EinsatzMonitor.OrderTeamsForMonitor([charlie, bravo, alpha]));

        charlie.StartTimer(now.AddMinutes(1));
        charlie.StopTimer();
        Assert.Equal(
            new[] { alpha, bravo, charlie },
            EinsatzMonitor.OrderTeamsForMonitor([charlie, bravo, alpha]));
    }

    [Fact]
    public async Task ImportRuntimeSnapshotAsync_LegacySnapshotKeepsStatusOrder()
    {
        var service = new EinsatzService();
        var ready = new Team { TeamName = "Bereit" };
        var pausing = new Team { TeamName = "Pause", IsPausing = true };
        var running = new Team { TeamName = "Aktiv", IsRunning = true };
        var snapshot = new EinsatzRuntimeSnapshot
        {
            Teams = [pausing, ready, running]
        };

        await service.ImportRuntimeSnapshotAsync(snapshot);

        Assert.Equal(
            new[] { running, ready, pausing },
            EinsatzMonitor.OrderTeamsForMonitor(service.Teams));
    }

    [Fact]
    public async Task ReorderTeamsAsync_PreservesOtherPositionsAndPersistsOrder()
    {
        var service = new EinsatzService();
        var alpha = new Team { TeamName = "Alpha" };
        var bravo = new Team { TeamName = "Bravo" };
        var charlie = new Team { TeamName = "Charlie" };
        var orderChanged = 0;

        await service.AddTeamAsync(alpha);
        await service.AddTeamAsync(bravo);
        await service.AddTeamAsync(charlie);
        service.TeamsReordered += () => orderChanged++;

        await service.ReorderTeamsAsync([charlie.TeamId, alpha.TeamId]);

        Assert.Equal(new[] { charlie, bravo, alpha }, EinsatzMonitor.OrderTeamsForMonitor(service.Teams));
        Assert.Equal(1, orderChanged);

        var exportedSnapshot = service.ExportRuntimeSnapshot();
        Assert.NotSame(service.Teams, exportedSnapshot.CurrentEinsatz.Teams);
        Assert.Equal(service.Teams.Count, exportedSnapshot.CurrentEinsatz.Teams.Count);
        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(exportedSnapshot);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<EinsatzRuntimeSnapshot>(snapshotJson)!;
        Assert.Equal(new[] { charlie.TeamId, bravo.TeamId, alpha.TeamId }, snapshot.TeamOrderIds);
        var restoredService = new EinsatzService();
        await restoredService.ImportRuntimeSnapshotAsync(snapshot);
        Assert.Equal(
            new[] { charlie.TeamId, bravo.TeamId, alpha.TeamId },
            EinsatzMonitor.OrderTeamsForMonitor(restoredService.Teams).Select(team => team.TeamId));

        var delta = new Team { TeamName = "Delta" };
        await service.AddTeamAsync(delta);
        Assert.Equal(new[] { charlie, bravo, alpha, delta }, EinsatzMonitor.OrderTeamsForMonitor(service.Teams));
        Assert.Equal(3, exportedSnapshot.CurrentEinsatz.Teams.Count);
    }

    [Fact]
    public async Task ReorderTeamsAsync_ConcurrentCallsKeepEveryTeamOnce()
    {
        var service = new EinsatzService();
        var teams = Enumerable.Range(0, 10)
            .Select(index => new Team { TeamName = $"Team {index}" })
            .ToArray();

        foreach (var team in teams)
            await service.AddTeamAsync(team);

        var reorderTasks = Enumerable.Range(0, teams.Length)
            .Select(offset =>
            {
                var ids = teams.Skip(offset).Concat(teams.Take(offset)).Select(team => team.TeamId).ToArray();
                return Task.Run(() => service.ReorderTeamsAsync(ids));
            });

        await Task.WhenAll(reorderTasks);

        var orderedTeams = EinsatzMonitor.OrderTeamsForMonitor(service.Teams).ToArray();
        Assert.Equal(teams.Length, orderedTeams.Select(team => team.TeamId).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, teams.Length), orderedTeams.Select(team => team.ManualOrderIndex ?? -1));
    }
}

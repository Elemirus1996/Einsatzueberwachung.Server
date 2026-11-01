using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Components.Pages;

namespace Einsatzueberwachung.Tests;

public class EinsatzMonitorTeamOrderingTests
{
    [Fact]
    public void OrderTeamsForMonitor_ManualPauseKeepsPositionButStopMovesTeamDown()
    {
        var alpha = new Team { TeamName = "Alpha" };
        var bravo = new Team { TeamName = "Bravo" };
        var charlie = new Team { TeamName = "Charlie" };
        var delta = new Team { TeamName = "Delta" };
        var echo = new Team { TeamName = "Echo" };
        var startTime = DateTime.Now.AddMinutes(-5);
        var now = DateTime.Now;

        alpha.StartTimer(startTime);
        bravo.StartTimer(startTime);
        charlie.StartTimer(startTime);
        delta.StartTimer(startTime);

        var orderBeforePause = EinsatzMonitor.OrderTeamsForMonitor([echo, delta, charlie, bravo, alpha]).ToArray();

        charlie.PauseTimer(now);

        Assert.Equal(orderBeforePause, EinsatzMonitor.OrderTeamsForMonitor([echo, delta, charlie, bravo, alpha]));

        charlie.StartTimer(now.AddMinutes(1));
        charlie.StopTimer();

        Assert.Equal(
            new[] { alpha, bravo, delta, charlie, echo },
            EinsatzMonitor.OrderTeamsForMonitor([echo, delta, charlie, bravo, alpha]));
    }
}

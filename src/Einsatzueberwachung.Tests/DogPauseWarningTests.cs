using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

public class DogPauseWarningTests
{
    [Fact]
    public async Task StoppingDogTeam_StillRecordsPauseWithoutDogPauseWarningRule()
    {
        var einsatz = new EinsatzService();
        var team = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Suchteam",
            DogId = "dog-1",
            IsRunning = true,
            ElapsedTime = TimeSpan.FromMinutes(13)
        });

        await einsatz.StopTeamTimerAsync(team.TeamId);

        Assert.True(team.IsPausing);
        Assert.NotNull(einsatz.GetDogPause(team.DogId));
        Assert.DoesNotContain(WarningRuleDefinition.All, rule => rule.Source == WarningRuleDefinition.Sources.DogPause);
    }
}

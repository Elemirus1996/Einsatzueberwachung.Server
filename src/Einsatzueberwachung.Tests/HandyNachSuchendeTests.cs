using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>Nach beendeter Suche gilt das Handy eines Teams nicht mehr als „live“ (Traccar sendet evtl. weiter).</summary>
public class HandyNachSuchendeTests
{
    [Fact]
    public void SucheBeendet_NurNachStopp_NichtInKurzpauseOderBereit()
    {
        var team = new Team { TeamName = "Rex" };
        Assert.False(team.IsSucheBeendet); // noch nie gesucht

        team.StartTimer();
        team.ElapsedTime = TimeSpan.FromMinutes(5);
        Assert.False(team.IsSucheBeendet); // laeuft

        team.PauseTimer();
        Assert.False(team.IsSucheBeendet); // Kurzpause: Laufweg geht weiter

        team.StartTimer();
        team.StopTimer();
        Assert.True(team.IsSucheBeendet);

        team.ResetTimer();
        Assert.False(team.IsSucheBeendet); // „Bereit“ fuer die naechste Suche
    }

    [Fact]
    public async Task NachStopp_PositionWeg_NeuePositionenIgnoriert_BeimStartWiederLive()
    {
        var einsatz = new EinsatzService();
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });
        var entfernt = new List<string>();
        einsatz.TeamPhoneLocationRemoved += entfernt.Add;

        await einsatz.UpdateTeamPhoneLocationAsync(team.TeamId, 51.0, 7.0);
        Assert.True(einsatz.PhoneLocations.ContainsKey(team.TeamId)); // vor der ersten Suche sichtbar

        await einsatz.StartTeamTimerAsync(team.TeamId);
        team.ElapsedTime = TimeSpan.FromMinutes(3);
        await einsatz.StopTeamTimerAsync(team.TeamId);

        Assert.False(einsatz.PhoneLocations.ContainsKey(team.TeamId));
        Assert.Equal([team.TeamId], entfernt);

        await einsatz.UpdateTeamPhoneLocationAsync(team.TeamId, 51.1, 7.0); // Traccar sendet noch
        Assert.False(einsatz.PhoneLocations.ContainsKey(team.TeamId));

        await einsatz.StartTeamTimerAsync(team.TeamId);
        await einsatz.UpdateTeamPhoneLocationAsync(team.TeamId, 51.2, 7.0);
        Assert.Equal(51.2, einsatz.PhoneLocations[team.TeamId].Latitude, 5);
        team.StopTimer();
    }

    [Fact]
    public async Task Kurzpause_LaesstHandyLive()
    {
        var einsatz = new EinsatzService();
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });

        await einsatz.StartTeamTimerAsync(team.TeamId);
        team.ElapsedTime = TimeSpan.FromMinutes(3);
        await einsatz.PauseTeamTimerAsync(team.TeamId);
        await einsatz.UpdateTeamPhoneLocationAsync(team.TeamId, 51.3, 7.0);

        Assert.True(einsatz.PhoneLocations.ContainsKey(team.TeamId));
    }
}

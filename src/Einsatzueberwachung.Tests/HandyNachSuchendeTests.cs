using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>Nach beendeter Suche gilt das Handy eines Teams nicht mehr als „live“ (Traccar sendet evtl. weiter).</summary>
public class HandyNachSuchendeTests
{
    [Fact]
    public void SucheBeendet_NurNachStopp_NichtInKurzpauseOderBereit()
    {
        // Feste Zeiten: Mit der echten Uhr lag zwischen Start und Pause oft keine messbare Zeit, ElapsedTime blieb 0
        var t0 = new DateTime(2026, 10, 28, 9, 0, 0);
        var team = new Team { TeamName = "Rex" };
        Assert.False(team.IsSucheBeendet); // noch nie gesucht

        team.StartTimer(t0);
        Assert.False(team.IsSucheBeendet); // laeuft

        team.PauseTimer(t0.AddMinutes(5));
        Assert.False(team.IsSucheBeendet); // Kurzpause: Laufweg geht weiter

        team.StartTimer(t0.AddMinutes(6));
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

/// <summary>Laufende Suche ueberlebt einen Neustart des Servers (Startzeit wird gespeichert und wiederhergestellt)</summary>
public class TeamStartzeitNachNeustartTests
{
    [Fact]
    public async Task LaufendesTeam_BehaeltStartzeitUeberDieSicherung()
    {
        var einsatz = new EinsatzService();
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });
        team.StartTimer(new DateTime(2026, 10, 27, 15, 0, 0));
        var json = System.Text.Json.JsonSerializer.Serialize(einsatz.ExportRuntimeSnapshot());

        var neu = new EinsatzService();
        await neu.ImportRuntimeSnapshotAsync(System.Text.Json.JsonSerializer.Deserialize<EinsatzRuntimeSnapshot>(json)!);

        Assert.Equal(new DateTime(2026, 10, 27, 15, 0, 0), neu.Teams.Single().StartTime);
        team.StopTimer();
        neu.Teams.Single().StopTimer();
    }

    [Fact]
    public void AeltereSicherungOhneStartzeit_WirdAusSuchzeitRekonstruiert()
    {
        var json = """{ "TeamName": "Rex", "IsRunning": true, "ElapsedTime": "00:42:00" }""";
        var team = System.Text.Json.JsonSerializer.Deserialize<Team>(json)!;
        Assert.Equal(default, team.StartTime);

        var jetzt = new DateTime(2026, 10, 27, 16, 0, 0);
        team.StartzeitWiederherstellen(jetzt);

        Assert.Equal(jetzt.AddMinutes(-42), team.StartTime);

        var kaputt = System.Text.Json.JsonSerializer.Deserialize<Team>("""{ "IsRunning": true, "ElapsedTime": "739915.17:22:19" }""")!;
        kaputt.StartzeitWiederherstellen(jetzt);
        Assert.Equal(TimeSpan.Zero, kaputt.ElapsedTime);
        Assert.Equal(jetzt, kaputt.StartTime);
    }
}

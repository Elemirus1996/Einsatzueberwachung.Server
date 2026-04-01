using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using System.IO;

namespace Einsatzueberwachung.Tests;

// ------------------------------------------------------------
// EinsatzService — Domain-Tests
// ------------------------------------------------------------
public class EinsatzServiceTests
{
    private static EinsatzService CreateService() => new();

    // --- Einsatz starten ---

    [Fact]
    public async Task StartEinsatz_SetsCurrentEinsatz()
    {
        var svc = CreateService();
        var data = new EinsatzData { Einsatzort = "Testort", IstEinsatz = true };

        await svc.StartEinsatzAsync(data);

        Assert.Equal("Testort", svc.CurrentEinsatz.Einsatzort);
    }

    [Fact]
    public async Task StartEinsatz_ClearsTeamsAndNotes()
    {
        var svc = CreateService();
        await svc.StartEinsatzAsync(new EinsatzData { Einsatzort = "A" });
        await svc.AddTeamAsync(new Team { TeamName = "T1" });
        await svc.AddGlobalNoteAsync("Notiz 1");

        // neuen Einsatz starten
        await svc.StartEinsatzAsync(new EinsatzData { Einsatzort = "B" });

        Assert.Empty(svc.Teams);
        // System-Startnote wird automatisch hinzugefügt
        Assert.True(svc.GlobalNotes.Count <= 1);
    }

    [Fact]
    public async Task StartEinsatz_FiresEinsatzChangedEvent()
    {
        var svc = CreateService();
        bool fired = false;
        svc.EinsatzChanged += () => fired = true;

        await svc.StartEinsatzAsync(new EinsatzData { Einsatzort = "Feuer" });

        Assert.True(fired);
    }

    [Fact]
    public async Task StartEinsatz_ParsesAlarmiertIntoAlarmierungsZeit()
    {
        var svc = CreateService();
        var data = new EinsatzData
        {
            Einsatzort = "ParseTest",
            Alarmiert = "01.04.2026 13:45"
        };

        await svc.StartEinsatzAsync(data);

        Assert.True(svc.CurrentEinsatz.AlarmierungsZeit.HasValue);
        Assert.Equal(new DateTime(2026, 4, 1, 13, 45, 0), svc.CurrentEinsatz.AlarmierungsZeit!.Value);
    }

    [Fact]
    public async Task StartEinsatz_UsesServerNowWhenAlarmMissing()
    {
        var svc = CreateService();
        var before = DateTime.Now;

        await svc.StartEinsatzAsync(new EinsatzData { Einsatzort = "NowFallback", Alarmiert = string.Empty });

        var after = DateTime.Now;
        Assert.True(svc.CurrentEinsatz.AlarmierungsZeit.HasValue);
        Assert.InRange(svc.CurrentEinsatz.AlarmierungsZeit!.Value, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.False(string.IsNullOrWhiteSpace(svc.CurrentEinsatz.Alarmiert));
    }

    // --- Team-Management ---

    [Fact]
    public async Task AddTeam_TeamIsAddedToList()
    {
        var svc = CreateService();
        var team = new Team { TeamName = "Alpha", FirstWarningMinutes = 30, SecondWarningMinutes = 60 };

        var added = await svc.AddTeamAsync(team);

        Assert.Single(svc.Teams);
        Assert.Equal("Alpha", added.TeamName);
    }

    [Fact]
    public async Task AddTeam_FiresTeamAddedEvent()
    {
        var svc = CreateService();
        Team? captured = null;
        svc.TeamAdded += t => captured = t;

        await svc.AddTeamAsync(new Team { TeamName = "Bravo" });

        Assert.NotNull(captured);
        Assert.Equal("Bravo", captured!.TeamName);
    }

    [Fact]
    public async Task RemoveTeam_ThrowsDuringActiveMission()
    {
        var svc = CreateService();
        await svc.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testort" });
        var t = await svc.AddTeamAsync(new Team { TeamName = "Charlie" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RemoveTeamAsync(t.TeamId));
    }

    [Fact]
    public async Task UpdateTeam_ChangesTeamName()
    {
        var svc = CreateService();
        var t = await svc.AddTeamAsync(new Team { TeamName = "Delta" });
        t.TeamName = "Delta 2";

        await svc.UpdateTeamAsync(t);

        Assert.Equal("Delta 2", svc.Teams.First().TeamName);
    }

    [Fact]
    public async Task GetTeamById_ReturnsCorrectTeam()
    {
        var svc = CreateService();
        var t = await svc.AddTeamAsync(new Team { TeamName = "Echo" });

        var result = await svc.GetTeamByIdAsync(t.TeamId);

        Assert.NotNull(result);
        Assert.Equal("Echo", result!.TeamName);
    }

    [Fact]
    public async Task GetTeamById_ReturnsNullForUnknownId()
    {
        var svc = CreateService();
        var result = await svc.GetTeamByIdAsync("nonexistent");
        Assert.Null(result);
    }

    // --- Timer ---

    [Fact]
    public async Task StartTimer_SetsIsRunning()
    {
        var svc = CreateService();
        var t = await svc.AddTeamAsync(new Team { TeamName = "Foxtrot" });

        await svc.StartTeamTimerAsync(t.TeamId);

        Assert.True(svc.Teams.First().IsRunning);
    }

    [Fact]
    public async Task StopTimer_ClearsIsRunning()
    {
        var svc = CreateService();
        var t = await svc.AddTeamAsync(new Team { TeamName = "Golf" });
        await svc.StartTeamTimerAsync(t.TeamId);

        await svc.StopTeamTimerAsync(t.TeamId);

        Assert.False(svc.Teams.First().IsRunning);
    }

    [Fact]
    public async Task ResetTimer_ResetsElapsedTime()
    {
        var svc = CreateService();
        var t = await svc.AddTeamAsync(new Team { TeamName = "Hotel" });
        await svc.StartTeamTimerAsync(t.TeamId);
        await Task.Delay(50);
        await svc.StopTeamTimerAsync(t.TeamId);

        await svc.ResetTeamTimerAsync(t.TeamId);

        Assert.Equal(TimeSpan.Zero, svc.Teams.First().ElapsedTime);
    }

    // --- Notizen ---

    [Fact]
    public async Task AddNote_NoteIsInList()
    {
        var svc = CreateService();

        await svc.AddGlobalNoteAsync("Testnotiz");

        Assert.Contains(svc.GlobalNotes, n => n.Text == "Testnotiz");
    }

    [Fact]
    public async Task AddNote_FiresNoteAddedEvent()
    {
        var svc = CreateService();
        GlobalNotesEntry? captured = null;
        svc.NoteAdded += n => captured = n;

        await svc.AddGlobalNoteAsync("Eventnotiz");

        Assert.NotNull(captured);
        Assert.Equal("Eventnotiz", captured!.Text);
    }

    [Fact]
    public async Task AddNote_TypeManualIsDefault()
    {
        var svc = CreateService();
        await svc.AddGlobalNoteAsync("Manual");

        var note = svc.GlobalNotes.Last();
        Assert.Equal(GlobalNotesEntryType.Manual, note.Type);
    }

    [Fact]
    public async Task GetFilteredNotes_FiltersByTeamId()
    {
        var svc = CreateService();
        await svc.AddGlobalNoteAsync("Global");
        await svc.AddGlobalNoteWithSourceAsync("Teamnotiz", "team1", "Alpha", "Mobile", createdBy: "Test");

        var result = await svc.GetFilteredNotesAsync("team1");

        // Service liefert globale (leere TeamId) UND team-spezifische Eintraege
        Assert.NotEmpty(result);
        Assert.Contains(result, n => n.SourceTeamId == "team1");
        // Keine fremden Teams in der Ergebnisliste
        Assert.DoesNotContain(result, n => !string.IsNullOrEmpty(n.SourceTeamId) && n.SourceTeamId != "team1");
    }

    // --- Suchgebiete ---

    [Fact]
    public async Task AddSearchArea_IsInCurrentEinsatz()
    {
        var svc = CreateService();
        await svc.StartEinsatzAsync(new EinsatzData { Einsatzort = "Wald" });
        var area = new SearchArea { Name = "Gebiet A", Color = "#ff0000" };

        var added = await svc.AddSearchAreaAsync(area);

        Assert.Contains(svc.CurrentEinsatz.SearchAreas, a => a.Id == added.Id);
    }

    [Fact]
    public async Task DeleteSearchArea_RemovesFromEinsatz()
    {
        var svc = CreateService();
        await svc.StartEinsatzAsync(new EinsatzData { Einsatzort = "See" });
        var area = await svc.AddSearchAreaAsync(new SearchArea { Name = "Zone 1" });

        await svc.DeleteSearchAreaAsync(area.Id);

        Assert.DoesNotContain(svc.CurrentEinsatz.SearchAreas, a => a.Id == area.Id);
    }

    [Fact]
    public async Task AssignTeamToArea_SetsSearchAreaOnTeam()
    {
        var svc = CreateService();
        await svc.StartEinsatzAsync(new EinsatzData { Einsatzort = "Feld" });
        var area = await svc.AddSearchAreaAsync(new SearchArea { Name = "Feld Nord" });
        var team = await svc.AddTeamAsync(new Team { TeamName = "Igel" });

        await svc.AssignTeamToSearchAreaAsync(area.Id, team.TeamId);

        Assert.Equal(area.Id, svc.Teams.First().SearchAreaId);
    }

    // --- Reset ---

    [Fact]
    public async Task ResetEinsatz_ClearsAllData()
    {
        var svc = CreateService();
        await svc.StartEinsatzAsync(new EinsatzData { Einsatzort = "Reset-Test" });
        await svc.AddTeamAsync(new Team { TeamName = "X" });
        await svc.AddGlobalNoteAsync("Note Y");

        svc.ResetEinsatz();

        Assert.Empty(svc.Teams);
        Assert.Empty(svc.GlobalNotes);
        Assert.True(string.IsNullOrEmpty(svc.CurrentEinsatz.Einsatzort));
    }
}

// ------------------------------------------------------------
// AppPathResolver — Plattform-Tests
// ------------------------------------------------------------
public class AppPathResolverTests
{
    [Fact]
    public void DataDir_IsNotNullOrEmpty()
    {
        var path = AppPathResolver.GetDataDirectory();
        Assert.False(string.IsNullOrWhiteSpace(path));
    }

    [Fact]
    public void ReportsDir_IsUnderDataDir()
    {
        var dataDir = AppPathResolver.GetDataDirectory();
        var reportsDir = AppPathResolver.GetReportDirectory();
        // Reports-Verzeichnis muss unter dem Datenbasisverzeichnis liegen
        Assert.StartsWith(dataDir.TrimEnd(Path.DirectorySeparatorChar),
                          reportsDir.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void ArchiveDir_IsUnderDataDir()
    {
        var dataDir = AppPathResolver.GetDataDirectory();
        var archiveDir = AppPathResolver.GetArchiveDirectory();
        Assert.StartsWith(dataDir.TrimEnd(Path.DirectorySeparatorChar),
                          archiveDir.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void DataDir_UsesEnvVar_WhenSet()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "einsatz-test-" + Guid.NewGuid());
        try
        {
            Environment.SetEnvironmentVariable("EINSATZUEBERWACHUNG_DATA_DIR", tmpDir);
            var path = AppPathResolver.GetDataDirectory();
            Assert.Equal(tmpDir, path.TrimEnd(Path.DirectorySeparatorChar));
        }
        finally
        {
            Environment.SetEnvironmentVariable("EINSATZUEBERWACHUNG_DATA_DIR", null);
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }
}

using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Components.Shared;

namespace Einsatzueberwachung.Tests;

public sealed class EinsatzResourceTests
{
    [Fact]
    public void VerfuegbarStatus_HasOperationalLabel()
    {
        Assert.Equal("Im Verfügungsraum", ResourceStatusPresentation.Label(ResourceStatus.Verfuegbar));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void TeamCardIndicator_IsShownForTrackedResourcesOnlyBeforeSearchStarts(
        bool resourceIsTracked,
        bool teamNotStarted,
        bool expected)
    {
        Assert.Equal(expected, ResourceStatusPresentation.ShouldShowTeamCardIndicator(resourceIsTracked, teamNotStarted));
    }

    [Fact]
    public void EveryResourceStatusHasAnIconAndLabel()
    {
        foreach (var status in Enum.GetValues<ResourceStatus>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ResourceStatusPresentation.Icon(status)));
            Assert.False(string.IsNullOrWhiteSpace(ResourceStatusPresentation.Label(status)));
        }
    }

    [Theory]
    [InlineData(ResourceEntityType.Person, "person-1")]
    [InlineData(ResourceEntityType.Dog, "dog-1")]
    public async Task CanAssignResourceToSearch_FallsBackToMasterDataWhenNoResourcesAreCheckedIn(
        ResourceEntityType entityType,
        string entityId)
    {
        var einsatz = await CreateActiveEinsatzAsync();

        Assert.Empty(einsatz.GetEinsatzResources());
        Assert.True(einsatz.CanAssignResourceToSearch(entityType, entityId));
    }

    [Fact]
    public async Task CanAssignResourceToSearch_OnlyAllowsCheckedInResourcesOnceResourceManagementIsUsed()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "person-1",
            Status = ResourceStatus.Verfuegbar
        });

        Assert.True(einsatz.CanAssignResourceToSearch(ResourceEntityType.Person, "person-1"));
        Assert.False(einsatz.CanAssignResourceToSearch(ResourceEntityType.Person, "person-2"));
        Assert.False(einsatz.CanAssignResourceToSearch(ResourceEntityType.Dog, "dog-1"));
    }

    [Fact]
    public async Task TeamAssignmentAndTimerWorkWhenNoResourcesAreCheckedIn()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        var team = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Suchteam",
            HundefuehrerId = "person-1",
            DogId = "dog-1",
            DogName = "Rex"
        });
        var area = await einsatz.AddSearchAreaAsync(new SearchArea { Name = "Sektor A" });

        await einsatz.AssignTeamToSearchAreaAsync(area.Id, team.TeamId);
        await einsatz.StartTeamTimerAsync(team.TeamId);

        Assert.Equal(team.TeamId, area.AssignedTeamId);
        Assert.True(team.IsRunning);
        Assert.Empty(einsatz.GetEinsatzResources());
    }

    [Fact]
    public async Task ResourceCanBeManuallyMarkedInSearch()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "person-1",
            Status = ResourceStatus.Verfuegbar
        });

        await einsatz.SetResourceStatusAsync(ResourceEntityType.Person, "person-1", ResourceStatus.InSuche);

        Assert.Equal(ResourceStatus.InSuche, Assert.Single(einsatz.GetEinsatzResources()).Status);
        Assert.False(einsatz.CanAssignResourceToSearch(ResourceEntityType.Person, "person-1"));
    }

    [Fact]
    public async Task Sonderauftrag_RequiresNoteAndCanBeChangedPerResource()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "person-1",
            Status = ResourceStatus.Verfuegbar
        });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            einsatz.SetResourceStatusAsync(ResourceEntityType.Person, "person-1", ResourceStatus.Sonderauftrag));
        Assert.Equal(ResourceStatus.Verfuegbar, Assert.Single(einsatz.GetEinsatzResources()).Status);

        await einsatz.SetResourceStatusAsync(
            ResourceEntityType.Person,
            "person-1",
            ResourceStatus.Sonderauftrag,
            "  Geruchsträgergewinnung  ");

        var resource = Assert.Single(einsatz.GetEinsatzResources());
        Assert.Equal(ResourceStatus.Sonderauftrag, resource.Status);
        Assert.Equal("Geruchsträgergewinnung", resource.SonderauftragNotiz);
        Assert.False(einsatz.CanAssignResourceToSearch(ResourceEntityType.Person, "person-1"));
    }

    [Fact]
    public async Task SearchAssignment_MarksResourcesInSearchAndRespectsDogPause()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "person-1",
            Status = ResourceStatus.Verfuegbar
        });
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Dog,
            EntityId = "dog-1",
            Status = ResourceStatus.Verfuegbar
        });
        var team = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Suchteam",
            HundefuehrerId = "person-1",
            DogId = "dog-1",
            DogName = "Rex"
        });
        var area = await einsatz.AddSearchAreaAsync(new SearchArea { Name = "Sektor A" });

        await einsatz.AssignTeamToSearchAreaAsync(area.Id, team.TeamId);

        Assert.All(einsatz.GetEinsatzResources(), resource => Assert.Equal(ResourceStatus.InSuche, resource.Status));
        Assert.False(einsatz.CanAssignResourceToSearch(ResourceEntityType.Person, "person-1"));
        await einsatz.StartTeamTimerAsync(team.TeamId);
        team.ElapsedTime = TimeSpan.FromMinutes(3);
        await einsatz.StopTeamTimerAsync(team.TeamId);

        var resources = einsatz.GetEinsatzResources().ToDictionary(resource => resource.EntityId);
        Assert.Equal(ResourceStatus.Verfuegbar, resources["person-1"].Status);
        Assert.Equal(ResourceStatus.Pause, resources["dog-1"].Status);

        await einsatz.ResetTeamTimerAsync(team.TeamId);
        Assert.Equal(ResourceStatus.Verfuegbar,
            einsatz.GetEinsatzResources().Single(resource => resource.EntityId == "dog-1").Status);
    }

    [Fact]
    public async Task SearchCannotUseCheckedOutResource()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "person-1",
            Status = ResourceStatus.AufAnfahrt
        });
        var team = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Suchteam",
            HundefuehrerId = "person-1"
        });
        var area = await einsatz.AddSearchAreaAsync(new SearchArea { Name = "Sektor A" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            einsatz.AssignTeamToSearchAreaAsync(area.Id, team.TeamId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            einsatz.StartTeamTimerAsync(team.TeamId));
    }

    private static async Task<EinsatzService> CreateActiveEinsatzAsync()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData
        {
            Einsatzort = "Testort",
            IstEinsatz = true
        });
        return einsatz;
    }
}

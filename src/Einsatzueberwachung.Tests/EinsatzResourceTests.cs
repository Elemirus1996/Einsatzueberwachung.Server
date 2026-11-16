using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

public sealed class EinsatzResourceTests
{
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

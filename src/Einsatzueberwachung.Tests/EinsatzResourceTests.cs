using Einsatzueberwachung.Domain.Interfaces;
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
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("a", true)]
    [InlineData("an", true)]
    [InlineData(" rhs ", true)]
    [InlineData("rex", true)]
    [InlineData("30 min", true)]
    [InlineData("unbekannt", false)]
    public void CheckInSearch_ShowsAllByDefaultAndRefinesOnAnyInput(string? query, bool expected) =>
        Assert.Equal(
            expected,
            ResourceCheckInSearch.MatchesQuery(query, "Anna Berg", "RHS Nord · Hund: Rex", "30 Minuten"));

    [Fact]
    public void ResourceRosterFilter_DoesNotMatchPeopleBasedOnTheirDogsStatus()
    {
        var person = new PersonalEntry { Id = "person-1", Vorname = "Alex", Nachname = "Muster" };
        var dog = new DogEntry { Id = "dog-1", Name = "Rex", HundefuehrerIds = ["person-1"] };
        var resources = new[]
        {
            new EinsatzResource
            {
                EntityType = ResourceEntityType.Person,
                EntityId = person.Id,
                Status = ResourceStatus.AufAnfahrt
            },
            new EinsatzResource
            {
                EntityType = ResourceEntityType.Dog,
                EntityId = dog.Id,
                Status = ResourceStatus.Verfuegbar
            }
        };
        var groups = ResourceRosterBuilder.Build(resources, [person], [dog]);

        var filtered = ResourceRosterFilter.Apply(
            groups,
            [dog],
            nameof(ResourceStatus.Verfuegbar),
            ResourceRosterFilter.AllHandlerAssignments,
            hideDogs: false);

        Assert.Empty(filtered);
    }

    [Fact]
    public void ResourceRosterFilter_ShowsDogWhenHandlerMatchesEvenIfDogDoesNot()
    {
        var handler = new PersonalEntry { Id = "handler", Vorname = "Alex", Nachname = "Muster" };
        var dog = new DogEntry { Id = "dog-1", Name = "Rex", HundefuehrerIds = [handler.Id] };
        var groups = ResourceRosterBuilder.Build(
            new[]
            {
                new EinsatzResource
                {
                    EntityType = ResourceEntityType.Person,
                    EntityId = handler.Id,
                    Status = ResourceStatus.Verfuegbar
                },
                new EinsatzResource
                {
                    EntityType = ResourceEntityType.Dog,
                    EntityId = dog.Id,
                    Status = ResourceStatus.VoruebergehendAbwesend
                }
            },
            [handler],
            [dog]);

        var filtered = ResourceRosterFilter.Apply(
            groups,
            [dog],
            nameof(ResourceStatus.Verfuegbar),
            ResourceRosterFilter.AllHandlerAssignments,
            hideDogs: false);

        var group = Assert.Single(filtered);
        Assert.True(group.ShowPrimaryStatus);
        var visibleDog = Assert.Single(group.Dogs);
        Assert.Equal(dog.Id, visibleDog.EntityId);
        Assert.Equal(ResourceStatus.VoruebergehendAbwesend, visibleDog.Status);

        var dogsHidden = ResourceRosterFilter.Apply(
            groups,
            [dog],
            nameof(ResourceStatus.Verfuegbar),
            ResourceRosterFilter.AllHandlerAssignments,
            hideDogs: true);

        var handlerOnlyGroup = Assert.Single(dogsHidden);
        Assert.True(handlerOnlyGroup.ShowPrimaryStatus);
        Assert.Empty(handlerOnlyGroup.Dogs);
    }

    [Fact]
    public void ResourceRosterFilter_AppliesPersonFiltersOnlyToPeople()
    {
        var handler = new PersonalEntry { Id = "handler", Vorname = "Alex", Nachname = "Muster" };
        var assignedDog = new DogEntry { Id = "assigned-dog", Name = "Rex", HundefuehrerIds = [handler.Id] };
        var standaloneDog = new DogEntry { Id = "standalone-dog", Name = "Flocke" };
        var groups = ResourceRosterBuilder.Build(
            new[]
            {
                new EinsatzResource
                {
                    EntityType = ResourceEntityType.Person,
                    EntityId = handler.Id,
                    Status = ResourceStatus.AufAnfahrt
                },
                new EinsatzResource
                {
                    EntityType = ResourceEntityType.Dog,
                    EntityId = assignedDog.Id,
                    Status = ResourceStatus.Verfuegbar
                },
                new EinsatzResource
                {
                    EntityType = ResourceEntityType.Dog,
                    EntityId = standaloneDog.Id,
                    Status = ResourceStatus.AufAnfahrt
                }
            },
            [handler],
            [assignedDog, standaloneDog]);

        var filtered = ResourceRosterFilter.Apply(
            groups,
            [assignedDog, standaloneDog],
            nameof(ResourceStatus.Verfuegbar),
            ResourceRosterFilter.HandlersOnly,
            hideDogs: false);

        var group = Assert.Single(filtered);
        Assert.Equal(ResourceEntityType.Dog, group.Primary.EntityType);
        Assert.Equal(standaloneDog.Id, group.Primary.EntityId);
    }

    [Fact]
    public void ResourceRosterFilter_AppliesHandlerAndDogVisibilityFilters()
    {
        var handler = new PersonalEntry { Id = "handler", Vorname = "Alex", Nachname = "Muster" };
        var personWithoutDog = new PersonalEntry { Id = "person", Vorname = "Kim", Nachname = "Beispiel" };
        var dog = new DogEntry { Id = "dog-1", Name = "Rex", HundefuehrerIds = [handler.Id] };
        var resources = new[]
        {
            new EinsatzResource
            {
                EntityType = ResourceEntityType.Person,
                EntityId = handler.Id,
                Status = ResourceStatus.Verfuegbar
            },
            new EinsatzResource
            {
                EntityType = ResourceEntityType.Person,
                EntityId = personWithoutDog.Id,
                Status = ResourceStatus.Verfuegbar
            },
            new EinsatzResource
            {
                EntityType = ResourceEntityType.Dog,
                EntityId = dog.Id,
                Status = ResourceStatus.Verfuegbar
            }
        };
        var groups = ResourceRosterBuilder.Build(resources, [handler, personWithoutDog], [dog]);

        var handlersOnly = ResourceRosterFilter.Apply(
            groups,
            [dog],
            ResourceRosterFilter.AllResourceStates,
            ResourceRosterFilter.HandlersOnly,
            hideDogs: false);
        var handlerGroup = Assert.Single(handlersOnly);
        Assert.Equal(handler.Id, handlerGroup.Primary.EntityId);
        Assert.Single(handlerGroup.Dogs);

        var peopleWithoutDogs = ResourceRosterFilter.Apply(
            groups,
            [dog],
            ResourceRosterFilter.AllResourceStates,
            ResourceRosterFilter.NoAssignedDog,
            hideDogs: true);
        Assert.Equal(personWithoutDog.Id, Assert.Single(peopleWithoutDogs).Primary.EntityId);
        Assert.Equal(2, ResourceRosterFilter.CountActiveFilters(
            ResourceRosterFilter.AllResourceStates,
            ResourceRosterFilter.NoAssignedDog,
            hideDogs: true));
    }

    [Theory]
    [InlineData("paw", "fa-solid fa-paw")]
    [InlineData("dog", "fa-solid fa-dog")]
    [InlineData("bone", "fa-solid fa-bone")]
    [InlineData("dot", "fa-solid fa-location-dot")]
    [InlineData("crosshairs", "fa-solid fa-crosshairs")]
    [InlineData("location-arrow", "fa-solid fa-location-arrow")]
    [InlineData("unknown", "fa-solid fa-paw")]
    public void DogIcon_UsesConfiguredCollarMarkerIcon(string setting, string expectedClass)
    {
        Assert.Equal(expectedClass, ResourceIconPresentation.CssClass(ResourceEntityType.Dog, setting));
    }

    [Fact]
    public void PersonIcon_DoesNotUseDogMarkerSetting()
    {
        Assert.Equal("bi bi-person", ResourceIconPresentation.CssClass(ResourceEntityType.Person, "dog"));
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
    public async Task RemoveResource_RemovesOnlyTheRequestedPersonOrDogAndNotifiesListeners()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "resource-1",
            Status = ResourceStatus.Verfuegbar
        });
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Dog,
            EntityId = "dog-1",
            Status = ResourceStatus.Verfuegbar
        });
        var resourcesChangedCount = 0;
        einsatz.ResourcesChanged += () => resourcesChangedCount++;

        await einsatz.RemoveResourceAsync(ResourceEntityType.Person, "resource-1");

        var remaining = Assert.Single(einsatz.GetEinsatzResources());
        Assert.Equal(ResourceEntityType.Dog, remaining.EntityType);
        Assert.Equal("dog-1", remaining.EntityId);
        Assert.Equal(1, resourcesChangedCount);

        await einsatz.RemoveResourceAsync(ResourceEntityType.Dog, "dog-1");

        Assert.Empty(einsatz.GetEinsatzResources());
        Assert.Equal(2, resourcesChangedCount);
    }

    [Fact]
    public async Task RemoveResource_RejectsResourcesAssignedToASearchArea()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "person-1",
            Status = ResourceStatus.Verfuegbar
        });
        var team = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Suchteam",
            HundefuehrerId = "person-1"
        });
        var area = await einsatz.AddSearchAreaAsync(new SearchArea { Name = "Sektor A" });
        await einsatz.AssignTeamToSearchAreaAsync(area.Id, team.TeamId);

        Assert.True(einsatz.IsResourceInActiveTeam(ResourceEntityType.Person, "person-1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            einsatz.RemoveResourceAsync(ResourceEntityType.Person, "person-1"));
        Assert.Single(einsatz.GetEinsatzResources());
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
    public async Task StoppingAndResettingTeam_PreservesSharedResourceStatusWhileOtherTeamRuns()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "person-1",
            Status = ResourceStatus.Verfuegbar
        });
        var firstTeam = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Suchteam 1",
            HundefuehrerId = "person-1"
        });
        var secondTeam = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Suchteam 2",
            HundefuehrerId = "person-1"
        });

        await einsatz.StartTeamTimerAsync(firstTeam.TeamId);
        await einsatz.StartTeamTimerIgnoringResourceAvailabilityAsync(secondTeam.TeamId);

        await einsatz.StopTeamTimerAsync(firstTeam.TeamId);
        Assert.Equal(ResourceStatus.InSuche, Assert.Single(einsatz.GetEinsatzResources()).Status);

        await einsatz.ResetTeamTimerAsync(firstTeam.TeamId);
        Assert.Equal(ResourceStatus.InSuche, Assert.Single(einsatz.GetEinsatzResources()).Status);

        await einsatz.StopTeamTimerAsync(secondTeam.TeamId);
        Assert.Equal(ResourceStatus.Verfuegbar, Assert.Single(einsatz.GetEinsatzResources()).Status);
    }

    [Fact]
    public async Task UnassigningTeam_PreservesSharedResourceStatusWhileOtherTeamRuns()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "person-1",
            Status = ResourceStatus.Verfuegbar
        });
        var assignedTeam = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Zugeordnetes Team",
            HundefuehrerId = "person-1"
        });
        var runningTeam = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Laufendes Team",
            HundefuehrerId = "person-1"
        });
        var area = await einsatz.AddSearchAreaAsync(new SearchArea { Name = "Sektor A" });
        await einsatz.AssignTeamToSearchAreaAsync(area.Id, assignedTeam.TeamId);
        await einsatz.StartTeamTimerIgnoringResourceAvailabilityAsync(runningTeam.TeamId);

        await einsatz.AssignTeamToSearchAreaAsync(area.Id, string.Empty);

        Assert.Equal(ResourceStatus.InSuche, Assert.Single(einsatz.GetEinsatzResources()).Status);
        await einsatz.StopTeamTimerAsync(runningTeam.TeamId);
        Assert.Equal(ResourceStatus.Verfuegbar, Assert.Single(einsatz.GetEinsatzResources()).Status);
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

        await Assert.ThrowsAsync<TeamResourceAvailabilityException>(() =>
            einsatz.AssignTeamToSearchAreaAsync(area.Id, team.TeamId));
        await Assert.ThrowsAsync<TeamResourceAvailabilityException>(() =>
            einsatz.StartTeamTimerAsync(team.TeamId));
    }

    [Fact]
    public async Task GetTeamStartResourceIssues_ReturnsAllUnavailableTeamResources()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "person-1",
            Status = ResourceStatus.Verfuegbar
        });
        await einsatz.SetResourceStatusAsync(
            ResourceEntityType.Person,
            "person-1",
            ResourceStatus.VoruebergehendAbwesend);
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Dog,
            EntityId = "dog-1",
            Status = ResourceStatus.AufAnfahrt
        });

        var team = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Suchteam",
            HundefuehrerId = "person-1",
            HelferIds = new List<string> { "person-2" },
            DogId = "dog-1"
        });

        Assert.Equal(
            new[]
            {
                new TeamResourceAvailabilityIssue(
                    ResourceEntityType.Person,
                    "person-1",
                    ResourceStatus.VoruebergehendAbwesend),
                new TeamResourceAvailabilityIssue(ResourceEntityType.Person, "person-2", null),
                new TeamResourceAvailabilityIssue(ResourceEntityType.Dog, "dog-1", ResourceStatus.AufAnfahrt)
            },
            einsatz.GetTeamStartResourceIssues(team.TeamId));
    }

    [Fact]
    public async Task StartTeamTimerAsync_ThrowsStructuredResourceAvailabilityException()
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

        var exception = await Assert.ThrowsAsync<TeamResourceAvailabilityException>(
            () => einsatz.StartTeamTimerAsync(team.TeamId));

        Assert.Equal("person-1", Assert.Single(exception.UnavailableResources).EntityId);
        Assert.False(team.IsRunning);
    }

    [Fact]
    public async Task StartTeamTimerIgnoringResourceAvailability_StartsAndMarksTeamResourcesInSearch()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "person-1",
            Status = ResourceStatus.Verfuegbar
        });
        await einsatz.SetResourceStatusAsync(
            ResourceEntityType.Person,
            "person-1",
            ResourceStatus.VoruebergehendAbwesend);
        var team = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Suchteam",
            HundefuehrerId = "person-1"
        });

        await einsatz.StartTeamTimerIgnoringResourceAvailabilityAsync(team.TeamId);

        Assert.True(team.IsRunning);
        Assert.Equal(
            ResourceStatus.InSuche,
            Assert.Single(einsatz.GetEinsatzResources()).Status);
    }

    [Theory]
    [InlineData(ResourceStatus.AufAnfahrt, ResourceStatus.AufAnfahrt)]
    [InlineData(ResourceStatus.Verfuegbar, ResourceStatus.Verfuegbar)]
    [InlineData(ResourceStatus.VoruebergehendAbwesend, ResourceStatus.Verfuegbar)]
    [InlineData(ResourceStatus.InSuche, ResourceStatus.Verfuegbar)]
    [InlineData(ResourceStatus.Sonderauftrag, ResourceStatus.Verfuegbar)]
    [InlineData(ResourceStatus.Drohneneinsatz, ResourceStatus.Verfuegbar)]
    [InlineData(ResourceStatus.Abgemeldet, ResourceStatus.Verfuegbar)]
    public void DeriveDogCheckInStatus_DerivesFromHandlerStatus(
        ResourceStatus handlerStatus,
        ResourceStatus expectedDogStatus)
    {
        Assert.Equal(expectedDogStatus, ResourceRosterBuilder.DeriveDogCheckInStatus(handlerStatus));
    }

    [Fact]
    public void ResourceRosterBuilder_GroupsMultipleDogsUnderHandlerWithOwnStateAndUncheckedCandidates()
    {
        var handler = new PersonalEntry { Id = "hf-1", Vorname = "Eric", Nachname = "Giemsa", IsActive = true };
        var dogAvailable = new DogEntry
        {
            Id = "dog-1",
            Name = "Karla",
            IsActive = true,
            HundefuehrerIds = new List<string> { "hf-1" }
        };
        var dogResting = new DogEntry
        {
            Id = "dog-2",
            Name = "Atlas",
            IsActive = true,
            HundefuehrerIds = new List<string> { "hf-1" }
        };
        var dogUnchecked = new DogEntry
        {
            Id = "dog-3",
            Name = "Bello",
            IsActive = true,
            HundefuehrerIds = new List<string> { "hf-1" }
        };
        var dogInactive = new DogEntry
        {
            Id = "dog-4",
            Name = "Inaktiv",
            IsActive = false,
            HundefuehrerIds = new List<string> { "hf-1" }
        };

        var resources = new[]
        {
            new EinsatzResource { EntityType = ResourceEntityType.Person, EntityId = "hf-1", Status = ResourceStatus.Verfuegbar },
            new EinsatzResource { EntityType = ResourceEntityType.Dog, EntityId = "dog-2", Status = ResourceStatus.Pause },
            new EinsatzResource { EntityType = ResourceEntityType.Dog, EntityId = "dog-1", Status = ResourceStatus.Verfuegbar }
        };

        var group = Assert.Single(ResourceRosterBuilder.Build(
            resources,
            new[] { handler },
            new[] { dogAvailable, dogResting, dogUnchecked, dogInactive }));

        Assert.Equal("Eric Giemsa", group.Primary.Name);
        Assert.Equal(ResourceStatus.Verfuegbar, group.Primary.Status);
        Assert.Collection(
            group.CheckedInDogs,
            first =>
            {
                Assert.Equal("Karla", first.Name);
                Assert.Equal(ResourceStatus.Verfuegbar, first.Status);
            },
            second =>
            {
                Assert.Equal("Atlas", second.Name);
                Assert.Equal(ResourceStatus.Pause, second.Status);
            });
        var candidate = Assert.Single(group.UncheckedActiveDogs);
        Assert.Equal("dog-3", candidate.DogId);
        Assert.Equal("Bello", candidate.Name);
    }

    [Fact]
    public async Task ResourceRosterBuilder_DisplaysSharedDogUnderEachCheckedInHandlerWithSharedState()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        var hf1 = new PersonalEntry { Id = "hf-1", Vorname = "Eric", Nachname = "Giemsa", IsActive = true };
        var hf2 = new PersonalEntry { Id = "hf-2", Vorname = "Lina", Nachname = "Giemsa", IsActive = true };
        var sharedDog = new DogEntry
        {
            Id = "dog-1",
            Name = "Karla",
            IsActive = true,
            HundefuehrerIds = new List<string> { "hf-1", "hf-2" }
        };

        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "hf-1",
            Status = ResourceStatus.Verfuegbar
        });
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "hf-2",
            Status = ResourceStatus.AufAnfahrt
        });

        var beforeDogCheckIn = ResourceRosterBuilder.Build(
            einsatz.GetEinsatzResources(),
            new[] { hf1, hf2 },
            new[] { sharedDog });

        Assert.Equal(2, beforeDogCheckIn.Count);
        Assert.All(beforeDogCheckIn, group =>
        {
            Assert.Empty(group.CheckedInDogs);
            Assert.Equal("dog-1", Assert.Single(group.UncheckedActiveDogs).DogId);
        });

        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Dog,
            EntityId = "dog-1",
            Status = ResourceStatus.Verfuegbar
        });
        await einsatz.SetResourceStatusAsync(ResourceEntityType.Dog, "dog-1", ResourceStatus.Pause);

        var afterDogCheckIn = ResourceRosterBuilder.Build(
            einsatz.GetEinsatzResources(),
            new[] { hf1, hf2 },
            new[] { sharedDog });

        Assert.Equal(3, einsatz.GetEinsatzResources().Count);
        Assert.Collection(
            afterDogCheckIn,
            firstHandler =>
            {
                Assert.Equal("Eric Giemsa", firstHandler.Primary.Name);
                Assert.Empty(firstHandler.UncheckedActiveDogs);
                var dog = Assert.Single(firstHandler.CheckedInDogs);
                Assert.Equal("Person:hf-1/Dog:dog-1", dog.UiKey);
                Assert.Equal(ResourceStatus.Pause, dog.Status);
            },
            secondHandler =>
            {
                Assert.Equal("Lina Giemsa", secondHandler.Primary.Name);
                Assert.Empty(secondHandler.UncheckedActiveDogs);
                var dog = Assert.Single(secondHandler.CheckedInDogs);
                Assert.Equal("Person:hf-2/Dog:dog-1", dog.UiKey);
                Assert.Equal(ResourceStatus.Pause, dog.Status);
            });
    }

    [Fact]
    public void ResourceRosterBuilder_SortsByPersonStatusAndKeepsStandaloneCheckedInDogVisible()
    {
        var personEnRoute = new PersonalEntry { Id = "hf-1", Vorname = "Anna", Nachname = "Alpha", IsActive = true };
        var personAvailable = new PersonalEntry { Id = "hf-2", Vorname = "Bernd", Nachname = "Beta", IsActive = true };
        var handlerDog = new DogEntry
        {
            Id = "dog-1",
            Name = "Karla",
            IsActive = true,
            HundefuehrerIds = new List<string> { "hf-1" }
        };
        var orphanDog = new DogEntry
        {
            Id = "dog-2",
            Name = "Gast-Rex",
            IsActive = true,
            HundefuehrerIds = new List<string> { "hf-not-checked-in" }
        };

        var resources = new[]
        {
            new EinsatzResource { EntityType = ResourceEntityType.Person, EntityId = "hf-1", Status = ResourceStatus.AufAnfahrt },
            new EinsatzResource { EntityType = ResourceEntityType.Dog, EntityId = "dog-1", Status = ResourceStatus.Verfuegbar },
            new EinsatzResource { EntityType = ResourceEntityType.Person, EntityId = "hf-2", Status = ResourceStatus.Verfuegbar },
            new EinsatzResource { EntityType = ResourceEntityType.Dog, EntityId = "dog-2", Status = ResourceStatus.VoruebergehendAbwesend }
        };

        var groups = ResourceRosterBuilder.Build(
            resources,
            new[] { personEnRoute, personAvailable },
            new[] { handlerDog, orphanDog });

        Assert.Collection(
            groups,
            first => Assert.Equal("Bernd Beta", first.Primary.Name),
            second =>
            {
                Assert.Equal("Gast-Rex", second.Primary.Name);
                Assert.Equal(ResourceEntityType.Dog, second.Primary.EntityType);
            },
            third =>
            {
                Assert.Equal("Anna Alpha", third.Primary.Name);
                Assert.Equal("Karla", Assert.Single(third.CheckedInDogs).Name);
            });
    }

    [Fact]
    public async Task PauseUndWeiter_OhneSuchgebiet_ErlaubtFortsetzenTrotzInSuche()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "hf-1",
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
            TeamName = "Mantrailer 1",
            HundefuehrerId = "hf-1",
            HundefuehrerName = "Anna Berg",
            DogId = "dog-1",
            DogName = "Rex"
        });

        await einsatz.StartTeamTimerAsync(team.TeamId);
        await einsatz.PauseTeamTimerAsync(team.TeamId);
        Assert.True(team.IsManuallyPaused);

        await einsatz.StartTeamTimerAsync(team.TeamId);
        Assert.True(team.IsRunning);
        Assert.False(team.IsManuallyPaused);
    }

    [Fact]
    public async Task SuchgebietAufhebenOderLoeschen_WaehrendHundepflichtpause_BehaeltPauseStatus()
    {
        var currentTime = new DateTime(2026, 11, 20, 10, 0, 0);
        var time = EinsatzAbschlussTests.Recorder<ITimeService>.Create((m, _) =>
            m.Name == "get_Now" ? currentTime : null);
        var einsatz = new EinsatzService(timeService: time.Proxy);
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testort", IstEinsatz = true });

        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "hf-1",
            Status = ResourceStatus.Verfuegbar
        });
        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Dog,
            EntityId = "dog-1",
            Status = ResourceStatus.Verfuegbar
        });

        var area = await einsatz.AddSearchAreaAsync(new SearchArea { Name = "Sektor Nord" });
        var team = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Fläche 1",
            HundefuehrerId = "hf-1",
            DogId = "dog-1",
            DogName = "Rex",
            PauseThresholdMinutes = 5,
            PauseMinutesShortRun = 20,
            PauseMinutesLongRun = 30
        });
        await einsatz.AssignTeamToSearchAreaAsync(area.Id, team.TeamId);

        await einsatz.StartTeamTimerAsync(team.TeamId);
        team.ElapsedTime = TimeSpan.FromMinutes(10);
        await einsatz.StopTeamTimerAsync(team.TeamId);

        Assert.True(team.IsPausing);
        await einsatz.DeleteSearchAreaAsync(area.Id);

        var resources = einsatz.GetEinsatzResources();
        Assert.Equal(ResourceStatus.Verfuegbar, resources.Single(r => r.EntityId == "hf-1").Status);
        Assert.Equal(ResourceStatus.Pause, resources.Single(r => r.EntityId == "dog-1").Status);
    }

    [Fact]
    public async Task AbgelaufeneHundepause_WirdEntferntUndUeberschreibtSpaetereManuellePauseNicht()
    {
        var currentTime = new DateTime(2026, 11, 20, 10, 0, 0);
        var time = EinsatzAbschlussTests.Recorder<ITimeService>.Create((m, _) =>
            m.Name == "get_Now" ? currentTime : null);
        var einsatz = new EinsatzService(timeService: time.Proxy);
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testort", IstEinsatz = true });

        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "hf-1",
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
            TeamName = "Fläche 1",
            HundefuehrerId = "hf-1",
            DogId = "dog-1",
            DogName = "Rex",
            PauseThresholdMinutes = 5,
            PauseMinutesShortRun = 1,
            PauseMinutesLongRun = 1
        });

        await einsatz.StartTeamTimerAsync(team.TeamId);
        team.ElapsedTime = TimeSpan.FromMinutes(10);
        await einsatz.StopTeamTimerAsync(team.TeamId);
        var pauseRecord = einsatz.GetDogPause("dog-1")!;
        pauseRecord.PauseStartTime = Ortszeit.Jetzt.AddMinutes(-5);

        einsatz.RefreshExpiredDogResourcePauses();
        Assert.Null(einsatz.GetDogPause("dog-1"));
        Assert.Equal(ResourceStatus.Verfuegbar, einsatz.GetEinsatzResources().Single(r => r.EntityId == "dog-1").Status);

        await einsatz.SetResourceStatusAsync(ResourceEntityType.Dog, "dog-1", ResourceStatus.Pause);
        einsatz.RefreshExpiredDogResourcePauses();
        Assert.Equal(ResourceStatus.Pause, einsatz.GetEinsatzResources().Single(r => r.EntityId == "dog-1").Status);
    }

    [Fact]
    public async Task StartWithCollarAsync_VerwendetKlarnamenInFehlermeldungUndLaesstHalsbandUnzugewiesen()
    {
        var einsatz = await CreateActiveEinsatzAsync();
        var collars = new CollarTrackingService(einsatz);
        await collars.ReceiveLocationAsync("HB-1", "Alpha", 51.4, 7.0, 90, DateTime.UtcNow);
        var time = EinsatzAbschlussTests.Recorder<ITimeService>.Create((m, _) =>
            m.Name == "get_Now" ? new DateTime(2026, 11, 20, 12, 0, 0) : null);
        var suche = new Einsatzueberwachung.Server.Services.TeamSucheService(einsatz, collars, time.Proxy);

        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = "guid-person-1",
            Status = ResourceStatus.AufAnfahrt
        });

        var team = await einsatz.AddTeamAsync(new Team
        {
            TeamName = "Team Alpha",
            HundefuehrerId = "guid-person-1",
            HundefuehrerName = "Anna Berg"
        });

        var ex = await Assert.ThrowsAsync<TeamResourceAvailabilityException>(() =>
            suche.StartWithCollarAsync(team.TeamId, "HB-1"));

        Assert.Contains("Anna Berg", ex.Message);
        Assert.DoesNotContain("guid-person-1", ex.Message);
        Assert.True(string.IsNullOrEmpty(team.CollarId));
        Assert.Contains(collars.GetAvailableCollars(), c => c.Id == "HB-1");
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

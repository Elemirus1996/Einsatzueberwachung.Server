using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Divera;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

public sealed class DiveraResourceSyncTests
{
    [Theory]
    [InlineData(1, "", true)]
    [InlineData(3, "", true)]
    [InlineData(56296, "", true)]
    [InlineData(56297, "", true)]
    [InlineData(0, "", false)]
    [InlineData(2, "", false)]
    [InlineData(56298, "", false)]
    [InlineData(70001, "#33aa33", true)]
    [InlineData(70002, "#cc3333", false)]
    [InlineData(70003, "", false)]
    public void IsPositive_UsesKnownResponsesAndCustomStatusColors(int status, string color, bool expected)
    {
        var response = new DiveraUcrEntry { Status = status, StatusColor = color };

        Assert.Equal(expected, response.IsPositive);
    }

    [Fact]
    public void ApplyDiveraAlarm_PreservesAlarmIdForResponseSynchronization()
    {
        var einsatz = new EinsatzData();
        var alarm = new DiveraAlarm { Id = 1234, Address = "Testort", Title = "Testalarm" };

        EinsatzStartHelper.ApplyDiveraAlarm(einsatz, alarm);

        Assert.Equal(1234, einsatz.DiveraAlarmId);
    }

    [Fact]
    public async Task Sync_ExposesPositiveResponsesWithoutAutoCheckingIn()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData
        {
            Einsatzort = "Testort",
            DiveraAlarmId = 1234,
            IstEinsatz = true
        });

        var mappedPerson = new PersonalEntry { Id = "person-positive", DiveraUserId = 42 };
        var negativePerson = new PersonalEntry { Id = "person-negative", DiveraUserId = 43 };
        var alarm = new DiveraAlarm
        {
            Id = 1234,
            UcrDetails =
            [
                new DiveraUcrEntry { MemberId = 42, Status = 1 },
                new DiveraUcrEntry { MemberId = 43, Status = 2 },
                new DiveraUcrEntry { MemberId = 99, Status = 1 }
            ]
        };
        var divera = EinsatzAbschlussTests.Recorder<IDiveraService>.Create((method, _) => method.Name switch
        {
            "add_DataChanged" or "remove_DataChanged" => null,
            "get_IsConfigured" => true,
            "get_PollIntervalActiveSeconds" => 60,
            "get_PollIntervalIdleSeconds" => 60,
            nameof(IDiveraService.GetActiveAlarmsAsync) => Task.FromResult(new List<DiveraAlarm> { alarm }),
            _ => throw new NotSupportedException(method.Name)
        });
        var masterData = EinsatzAbschlussTests.Recorder<IMasterDataService>.Create((method, _) => method.Name switch
        {
            nameof(IMasterDataService.GetPersonalListAsync) => Task.FromResult(new List<PersonalEntry>
            {
                mappedPerson,
                negativePerson
            }),
            _ => throw new NotSupportedException(method.Name)
        });

        var sync = new DiveraResourceSyncService(
            einsatz,
            divera.Proxy,
            masterData.Proxy,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DiveraResourceSyncService>.Instance);
        var responsesChangedCount = 0;
        sync.ResponsesChanged += () => responsesChangedCount++;

        await sync.StartAsync(CancellationToken.None);
        try
        {
            Assert.Empty(einsatz.GetEinsatzResources());
            Assert.Equal(1, responsesChangedCount);

            var responses = sync.GetPositiveResponsesByPersonId();
            var singleResponse = Assert.Single(responses);
            Assert.Equal(mappedPerson.Id, singleResponse.Key);
            Assert.Equal("Kommt", singleResponse.Value);
        }
        finally
        {
            await sync.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BulkCheckIn_ChecksInOnlyPendingPositiveResponsesAndPreservesExistingResources()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData
        {
            Einsatzort = "Testort",
            DiveraAlarmId = 1234,
            IstEinsatz = true
        });

        var pendingPerson = new PersonalEntry { Id = "person-pending", DiveraUserId = 42 };
        var alreadyAvailablePerson = new PersonalEntry { Id = "person-available", DiveraUserId = 43 };
        var negativePerson = new PersonalEntry { Id = "person-negative", DiveraUserId = 44 };

        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = alreadyAvailablePerson.Id,
            Status = ResourceStatus.Verfuegbar
        });

        var alarm = new DiveraAlarm
        {
            Id = 1234,
            UcrDetails =
            [
                new DiveraUcrEntry { MemberId = 42, Status = 56296 },
                new DiveraUcrEntry { MemberId = 43, Status = 1 },
                new DiveraUcrEntry { MemberId = 44, Status = 56298 }
            ]
        };
        var divera = EinsatzAbschlussTests.Recorder<IDiveraService>.Create((method, _) => method.Name switch
        {
            nameof(IDiveraService.GetActiveAlarmsAsync) => Task.FromResult(new List<DiveraAlarm> { alarm }),
            _ => throw new NotSupportedException(method.Name)
        });
        var masterData = EinsatzAbschlussTests.Recorder<IMasterDataService>.Create((method, _) => method.Name switch
        {
            nameof(IMasterDataService.GetPersonalListAsync) => Task.FromResult(new List<PersonalEntry>
            {
                pendingPerson,
                alreadyAvailablePerson,
                negativePerson
            }),
            _ => throw new NotSupportedException(method.Name)
        });

        var sync = new DiveraResourceSyncService(
            einsatz,
            divera.Proxy,
            masterData.Proxy,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DiveraResourceSyncService>.Instance);

        var checkedInCount = await sync.CheckInPendingPositiveResponsesAsync();

        Assert.Equal(1, checkedInCount);
        var resources = einsatz.GetEinsatzResources();
        Assert.Equal(2, resources.Count);

        var existingResource = Assert.Single(resources, r => r.EntityId == alreadyAvailablePerson.Id);
        Assert.Equal(ResourceStatus.Verfuegbar, existingResource.Status);

        var addedResource = Assert.Single(resources, r => r.EntityId == pendingPerson.Id);
        Assert.Equal(ResourceStatus.AufAnfahrt, addedResource.Status);
        Assert.Equal("30 Minuten", addedResource.DiveraRueckmeldung);
    }

    [Fact]
    public async Task Sync_UpdatesDiveraRueckmeldungForAlreadyCheckedInEnRoutePersonOnly()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData
        {
            Einsatzort = "Testort",
            DiveraAlarmId = 1234,
            IstEinsatz = true
        });

        var checkedInEnRoute = new PersonalEntry { Id = "person-enroute", DiveraUserId = 42 };
        var uncheckedPositive = new PersonalEntry { Id = "person-unchecked", DiveraUserId = 43 };

        await einsatz.CheckInResourceAsync(new EinsatzResource
        {
            EntityType = ResourceEntityType.Person,
            EntityId = checkedInEnRoute.Id,
            Status = ResourceStatus.AufAnfahrt,
            DiveraRueckmeldung = "1 Stunde"
        });

        var alarm = new DiveraAlarm
        {
            Id = 1234,
            UcrDetails =
            [
                new DiveraUcrEntry { MemberId = 42, Status = 56296 },
                new DiveraUcrEntry { MemberId = 43, Status = 1 }
            ]
        };
        var divera = EinsatzAbschlussTests.Recorder<IDiveraService>.Create((method, _) => method.Name switch
        {
            nameof(IDiveraService.GetActiveAlarmsAsync) => Task.FromResult(new List<DiveraAlarm> { alarm }),
            _ => throw new NotSupportedException(method.Name)
        });
        var masterData = EinsatzAbschlussTests.Recorder<IMasterDataService>.Create((method, _) => method.Name switch
        {
            nameof(IMasterDataService.GetPersonalListAsync) => Task.FromResult(new List<PersonalEntry>
            {
                checkedInEnRoute,
                uncheckedPositive
            }),
            _ => throw new NotSupportedException(method.Name)
        });

        var sync = new DiveraResourceSyncService(
            einsatz,
            divera.Proxy,
            masterData.Proxy,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DiveraResourceSyncService>.Instance);

        await sync.SyncActiveAlarmResponsesAsync();

        var singleResource = Assert.Single(einsatz.GetEinsatzResources());
        Assert.Equal(checkedInEnRoute.Id, singleResource.EntityId);
        Assert.Equal(ResourceStatus.AufAnfahrt, singleResource.Status);
        Assert.Equal("30 Minuten", singleResource.DiveraRueckmeldung);
        Assert.Equal(2, sync.GetPositiveResponsesByPersonId().Count);
    }

    [Fact]
    public async Task LocalTestSimulation_WorksWithoutRealDiveraCallsOrConfiguredDiveraUserIds()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData
        {
            Einsatzort = "Übungsort",
            IstEinsatz = true
        });

        var anna = new PersonalEntry { Id = "person-anna", Vorname = "Anna", Nachname = "Albers", DiveraUserId = null };
        var ben = new PersonalEntry { Id = "person-ben", Vorname = "Ben", Nachname = "Becker", DiveraUserId = null };
        var clara = new PersonalEntry { Id = "person-clara", Vorname = "Clara", Nachname = "Cramer", DiveraUserId = 77 };
        var david = new PersonalEntry { Id = "person-david", Vorname = "David", Nachname = "Dorn", DiveraUserId = null };
        var personnel = new List<PersonalEntry> { anna, ben, clara, david };

        var innerDiveraCalled = false;
        var innerDivera = EinsatzAbschlussTests.Recorder<IDiveraService>.Create((method, _) =>
        {
            if (method.Name is "add_DataChanged" or "remove_DataChanged")
                return null;
            if (method.Name == "get_IsConfigured")
                return false;
            if (method.Name == "get_HasActiveAlarms")
                return false;
            if (method.Name == "get_PollIntervalIdleSeconds")
                return 600;
            if (method.Name == "get_PollIntervalActiveSeconds")
                return 60;

            innerDiveraCalled = true;
            throw new InvalidOperationException($"Echter DiveraService darf im Testmodus nicht aufgerufen werden ({method.Name}).");
        });

        var masterData = EinsatzAbschlussTests.Recorder<IMasterDataService>.Create((method, _) => method.Name switch
        {
            nameof(IMasterDataService.GetPersonalListAsync) => Task.FromResult(personnel),
            _ => throw new NotSupportedException(method.Name)
        });
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((method, _) => method.Name switch
        {
            nameof(ISettingsService.GetStaffelSettingsAsync) => Task.FromResult(new StaffelSettings
            {
                StaffelAdresse = "Testweg 1, Musterstadt"
            }),
            _ => throw new NotSupportedException(method.Name)
        });
        var time = EinsatzAbschlussTests.Recorder<ITimeService>.Create((method, _) => method.Name switch
        {
            "get_Now" => new DateTime(2026, 11, 19, 12, 0, 0),
            _ => throw new NotSupportedException(method.Name)
        });

        var simulation = new DiveraTestSimulationService(
            masterData.Proxy,
            settings.Proxy,
            einsatz,
            time.Proxy,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DiveraTestSimulationService>.Instance);
        using var simulatableDivera = new SimulatableDiveraService(innerDivera.Proxy, simulation);
        var sync = new DiveraResourceSyncService(
            einsatz,
            simulatableDivera,
            masterData.Proxy,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DiveraResourceSyncService>.Instance,
            simulation);

        Assert.False(simulatableDivera.IsConfigured);

        var summary = await simulation.StartTestAlarmAsync();

        Assert.True(simulation.IsActive);
        Assert.True(simulatableDivera.IsConfigured);
        Assert.True(simulatableDivera.HasActiveAlarms);
        Assert.True(summary.LinkedActiveEinsatz);
        Assert.Equal(DiveraTestSimulationService.SimulatedAlarmId, einsatz.CurrentEinsatz.DiveraAlarmId);
        Assert.Equal(3, summary.PositiveResponseCount);
        Assert.Equal(1, summary.NegativeResponseCount);
        Assert.Null(anna.DiveraUserId);
        Assert.Null(ben.DiveraUserId);
        Assert.Equal(77, clara.DiveraUserId);

        var alarms = await simulatableDivera.GetActiveAlarmsAsync();
        var activeAlarm = Assert.Single(alarms);
        Assert.Equal("Testweg 1, Musterstadt", activeAlarm.Address);
        Assert.False(innerDiveraCalled);

        await sync.SyncActiveAlarmResponsesAsync();
        var positiveResponses = sync.GetPositiveResponsesByPersonId();
        Assert.Equal(3, positiveResponses.Count);
        Assert.Equal("30 Minuten", positiveResponses[anna.Id]);
        Assert.Equal("1 Stunde", positiveResponses[ben.Id]);
        Assert.Equal("30 Minuten", positiveResponses[clara.Id]);
        Assert.False(positiveResponses.ContainsKey(david.Id));

        var checkedIn = await sync.CheckInPendingPositiveResponsesAsync();
        Assert.Equal(3, checkedIn);

        await simulation.StopTestAlarmAsync();
        await sync.SyncActiveAlarmResponsesAsync();

        Assert.False(simulation.IsActive);
        Assert.False(simulatableDivera.IsConfigured);
        Assert.Null(einsatz.CurrentEinsatz.DiveraAlarmId);
        Assert.Empty(sync.GetPositiveResponsesByPersonId());
        Assert.False(innerDiveraCalled);
    }
}

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
    public async Task Sync_ChecksInOnlyMappedPeopleWithPositiveResponses()
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
        await sync.StartAsync(CancellationToken.None);
        try
        {
            var resource = Assert.Single(einsatz.GetEinsatzResources());
            Assert.Equal(ResourceEntityType.Person, resource.EntityType);
            Assert.Equal(mappedPerson.Id, resource.EntityId);
            Assert.Equal(ResourceStatus.AufAnfahrt, resource.Status);
            Assert.Equal("Kommt", resource.DiveraRueckmeldung);
        }
        finally
        {
            await sync.StopAsync(CancellationToken.None);
        }
    }
}

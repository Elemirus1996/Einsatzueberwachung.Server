using System.Reflection;
using System.Text;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.LiveTracking;
using Einsatzueberwachung.LiveTracking.Services;
using Einsatzueberwachung.Server.Hubs;
using Einsatzueberwachung.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

public class GarminCollarBatteryTests
{
    // Status bytes from the supplied captures; location/time are deliberately omitted.
    [Theory]
    [InlineData("21000000FFFF100102030305FFFFCD", "Hund 1", 3)]
    [InlineData("21000000FFFF0D040B030205FFFFCB", "Hund 2", 3)]
    [InlineData("21000000FFFF0D040B040205FFFFCB", "Hund 2", 4)]
    [InlineData("23000000FFFF13030C020305FFFFCC", "Hund 3", 2)]
    public void CapturedStatus_DecodesBatteryFromByte25(string status, string name, int expected)
    {
        var payload = new byte[100];
        Convert.FromHexString(status).CopyTo(payload, 16);
        Encoding.ASCII.GetBytes(name).CopyTo(payload, 31);

        var collar = Assert.IsType<DogCollarData>(DogCollarData.FromPayload(payload));

        Assert.Equal(name, collar.DogName);
        Assert.Equal(expected, collar.BatteryLevel);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, null)]
    [InlineData(255, null)]
    public void BatteryLevel_IsIndependentOfStatusA_AndInvalidValuesAreUnknown(byte value, int? expected)
    {
        var payload = new byte[100];
        payload[25] = value;
        Encoding.ASCII.GetBytes("Hund").CopyTo(payload, 31);

        foreach (var status in new byte[] { 0x21, 0x22, 0x23 })
        {
            payload[16] = status;
            Assert.Equal(expected, DogCollarData.FromPayload(payload)!.BatteryLevel);
        }
    }

    [Fact]
    public void Simulation_ProducesFullBattery()
    {
        var method = typeof(GpsSimulationService).GetMethod("BuildDogCollarPayload", BindingFlags.NonPublic | BindingFlags.Static)!;
        var payload = (byte[])method.Invoke(null, [(short)901, "Simulation", 51.0, 7.0, 58.0f])!;

        Assert.Equal(4, DogCollarData.FromPayload(payload)!.BatteryLevel);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(null, false)]
    public async Task AssignedRunningCollar_WarnsOnlyAtLevelOne(int? level, bool expectWarning)
    {
        var einsatz = new EinsatzService();
        using var collars = new CollarTrackingService(einsatz);
        var team = await einsatz.AddTeamAsync(new Team { TeamName = "Rex" });
        await collars.AssignCollarToTeamAsync("C-1", team.TeamId);
        team.StartTimer();

        var warnings = new List<WarningEntry>();
        var warningService = EinsatzAbschlussTests.Recorder<IWarningService>.Create((method, args) =>
        {
            if (method.Name == nameof(IWarningService.AddWarning)) warnings.Add((WarningEntry)args![0]!);
            return method.Name == nameof(IWarningService.GetRuleConfig) ? new WarningRuleConfig() : null;
        });
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((_, _) => null);
        var time = new FixedTime();
        using var services = new ServiceCollection().AddLogging().AddSignalR().Services.BuildServiceProvider();
        var relay = new CollarTrackingRelayService(collars, einsatz, settings.Proxy, warningService.Proxy, time,
            services.GetRequiredService<IHubContext<EinsatzHub>>(), NullLogger<CollarTrackingRelayService>.Instance);

        await relay.StartAsync(CancellationToken.None);
        try
        {
            await collars.ReceiveLocationAsync("C-1", "Halsband", 51, 7, level);
            if (expectWarning)
            {
                var warning = Assert.Single(warnings);
                Assert.Equal(WarningRuleDefinition.Sources.CollarLowBattery, warning.Source);
                Assert.Contains("1/4", warning.Message);
            }
            else
            {
                Assert.Empty(warnings);
            }
        }
        finally
        {
            await relay.StopAsync(CancellationToken.None);
            team.StopTimer();
        }
    }

    private sealed class FixedTime : ITimeService
    {
        public DateTime Now => DateTime.Now;
        public void Refresh() { }
    }
}
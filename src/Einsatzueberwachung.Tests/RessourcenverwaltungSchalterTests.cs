using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>Schalter „Ressourcenverwaltung verwenden“ und Check-in auch in Übungen (#168)</summary>
public sealed class RessourcenverwaltungSchalterTests
{
    private static EinsatzResource Person(string id = "person-1") => new()
    {
        EntityType = ResourceEntityType.Person,
        EntityId = id,
        Status = ResourceStatus.AufAnfahrt
    };

    private static async Task<EinsatzService> StarteAsync(bool aktiv, bool istEinsatz = true)
    {
        var einstellungen = new AppSettings { RessourcenverwaltungAktiv = aktiv };
        var settings = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) => m.Name switch
        {
            nameof(ISettingsService.GetAppSettingsAsync) => Task.FromResult(einstellungen),
            nameof(ISettingsService.GetStaffelSettingsAsync) => Task.FromResult(new StaffelSettings()),
            _ => null
        });
        var einsatz = new EinsatzService(settings.Proxy);
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testort", IstEinsatz = istEinsatz });
        return einsatz;
    }

    [Fact]
    public async Task Ausgeschaltet_CheckInWirdAbgelehnt()
    {
        var einsatz = await StarteAsync(aktiv: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => einsatz.CheckInResourceAsync(Person()));
        Assert.Contains("ausgeschaltet", ex.Message);
        Assert.Empty(einsatz.GetEinsatzResources());
    }

    [Fact]
    public async Task Eingeschaltet_CheckInInUebungMoeglich()
    {
        var einsatz = await StarteAsync(aktiv: true, istEinsatz: false);

        await einsatz.CheckInResourceAsync(Person());

        Assert.Single(einsatz.GetEinsatzResources());
    }

    [Fact]
    public async Task Ausschalten_VerwirftCheckInsUndHebtStartsperreAuf()
    {
        var einsatz = await StarteAsync(aktiv: true);
        await einsatz.CheckInResourceAsync(Person());
        var meldungen = 0;
        einsatz.ResourcesChanged += () => meldungen++;

        einsatz.ClearEinsatzResources();

        Assert.Empty(einsatz.GetEinsatzResources());
        Assert.Equal(1, meldungen);
    }

    [Fact]
    public void Standard_IstAusgeschaltet()
    {
        Assert.False(new AppSettings().RessourcenverwaltungAktiv);
    }
}

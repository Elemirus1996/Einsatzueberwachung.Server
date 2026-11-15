using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;
using Xunit;

namespace Einsatzueberwachung.Tests;

public class VermisstenMultiTests
{
    [Fact]
    public async Task Upsert_addsNewEntry_andSetsTimestamp()
    {
        var svc = new EinsatzService();
        var info = new VermisstenInfo { Vorname = "Anna", Nachname = "Müller" };

        await svc.UpsertVermisstenAsync(info);

        Assert.Single(svc.CurrentEinsatz.Vermisste);
        Assert.NotEqual(Guid.Empty, svc.CurrentEinsatz.Vermisste[0].Id);
        Assert.NotNull(svc.CurrentEinsatz.Vermisste[0].ZuletztAktualisiert);
    }

    [Fact]
    public async Task Upsert_updatesExisting_byId()
    {
        var svc = new EinsatzService();
        var first = new VermisstenInfo { Id = Guid.NewGuid(), Vorname = "Anna" };
        await svc.UpsertVermisstenAsync(first);
        first.Vorname = "Annette";
        await svc.UpsertVermisstenAsync(first);

        Assert.Single(svc.CurrentEinsatz.Vermisste);
        Assert.Equal("Annette", svc.CurrentEinsatz.Vermisste[0].Vorname);
    }

    [Fact]
    public async Task Upsert_addsSecondPerson_inFlaecheScenario()
    {
        var svc = new EinsatzService();
        await svc.UpdateSzenarioAsync(EinsatzSzenarioType.Flaeche);
        await svc.UpsertVermisstenAsync(new VermisstenInfo { Vorname = "P1" });
        await svc.UpsertVermisstenAsync(new VermisstenInfo { Vorname = "P2" });

        Assert.Equal(2, svc.CurrentEinsatz.Vermisste.Count);
        Assert.Equal(new[] { "P1", "P2" }, svc.CurrentEinsatz.Vermisste.Select(v => v.Vorname));
    }

    [Fact]
    public async Task Upsert_secondPerson_inMantrailerScenario_throws()
    {
        var svc = new EinsatzService();
        await svc.UpdateSzenarioAsync(EinsatzSzenarioType.Mantrailer);
        await svc.UpsertVermisstenAsync(new VermisstenInfo { Vorname = "P1" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertVermisstenAsync(new VermisstenInfo { Vorname = "P2" }));
    }

    [Fact]
    public async Task Remove_dropsEntry_andFiresEvent()
    {
        var svc = new EinsatzService();
        var info = new VermisstenInfo { Vorname = "Anna" };
        await svc.UpsertVermisstenAsync(info);

        Guid? removedId = null;
        svc.VermisstenRemoved += id => removedId = id;

        await svc.RemoveVermisstenAsync(info.Id);

        Assert.Empty(svc.CurrentEinsatz.Vermisste);
        Assert.Equal(info.Id, removedId);
    }

    [Fact]
    public void LegacyJson_withSingleVermisstenInfo_migratesToList()
    {
        // Snapshot vor Multi-Vermissten-Migration: nur "VermisstenInfo" als Single-Objekt.
        var legacyJson = """
        {
          "Einsatzort": "Musterstadt",
          "VermisstenInfo": {
            "Vorname": "Otto",
                        "Nachname": "Normal",
                        "PolizeiKontaktName": "Alt-Kontakt",
                        "PolizeiTelefon": "01234",
                        "BosEinheit": "Alt-BOS",
                        "BosAufgabenteilung": "Absicherung"
          }
        }
        """;

        var data = JsonSerializer.Deserialize<EinsatzData>(legacyJson)!;

        Assert.Single(data.Vermisste);
        Assert.Equal("Otto", data.Vermisste[0].Vorname);
        Assert.NotNull(data.VermisstenInfo);
        Assert.Equal("Otto", data.VermisstenInfo!.Vorname);
        Assert.False(data.EinsatzkontakteMigriert);
        Assert.Equal("Alt-Kontakt", data.VermisstenInfo.PolizeiKontaktName);
        Assert.Equal("01234", data.VermisstenInfo.PolizeiTelefon);
        Assert.Equal("Alt-BOS", data.VermisstenInfo.BosEinheit);
        Assert.Equal("Absicherung", data.VermisstenInfo.BosAufgabenteilung);
    }

    [Fact]
    public async Task UpdateSzenarioAsync_setsField_andFiresEvent()
    {
        var svc = new EinsatzService();
        var fired = false;
        svc.SzenarioChanged += () => fired = true;

        await svc.UpdateSzenarioAsync(EinsatzSzenarioType.Truemmer);

        Assert.Equal(EinsatzSzenarioType.Truemmer, svc.CurrentEinsatz.Szenario);
        Assert.True(fired);
    }

    [Fact]
    public void EinsatzSzenarioType_AllowsMultiple_correctMapping()
    {
        Assert.True(EinsatzSzenarioType.Flaeche.AllowsMultipleVermisste());
        Assert.True(EinsatzSzenarioType.Truemmer.AllowsMultipleVermisste());
        Assert.False(EinsatzSzenarioType.Mantrailer.AllowsMultipleVermisste());
        Assert.False(EinsatzSzenarioType.Sonstige.AllowsMultipleVermisste());
        Assert.False(EinsatzSzenarioType.Unbestimmt.AllowsMultipleVermisste());
    }
}

using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

public class EinsatzKontaktMigrationTests
{
    [Fact]
    public async Task StartEinsatzAsync_MigratesLegacyPersonContactsToEinsatz()
    {
        var einsatz = new EinsatzData
        {
            PolizeiKontaktName = "Einsatzkontakt",
            BosEinheit = "Neue BOS-Einheit",
            Vermisste =
            [
                new VermisstenInfo
                {
                    PolizeiKontaktName = "Alter Kontakt",
                    PolizeiDienstnummer = "P-123",
                    PolizeiTelefon = "112",
                    PolizeiVermisstenmeldungAufgenommen = true,
                    PolizeiKoordinationBesprochen = true,
                    PolizeiSuchabschnittAbgestimmt = true,
                    PolizeiRueckmeldepflichtVereinbart = true,
                    PolizeiDatenschutzGeklaert = true,
                    BosEinheit = "Alte BOS-Einheit",
                    BosZugfuehrer = "Zugführer",
                    BosFunkrufname = "Florian 1",
                    BosAufgabenteilung = "Absicherung",
                    BosAbschnittAbgestimmt = true,
                    BosRessourcenBesprochen = true
                }
            ]
        };
        var service = new EinsatzService();

        await service.StartEinsatzAsync(einsatz);

        var current = service.CurrentEinsatz;
        Assert.True(current.EinsatzkontakteMigriert);
        Assert.Equal("Einsatzkontakt", current.PolizeiKontaktName);
        Assert.Equal("P-123", current.PolizeiDienstnummer);
        Assert.Equal("112", current.PolizeiTelefon);
        Assert.True(current.PolizeiVermisstenmeldungAufgenommen);
        Assert.True(current.PolizeiKoordinationBesprochen);
        Assert.True(current.PolizeiSuchabschnittAbgestimmt);
        Assert.True(current.PolizeiRueckmeldepflichtVereinbart);
        Assert.True(current.PolizeiDatenschutzGeklaert);
        Assert.Equal("Neue BOS-Einheit", current.BosEinheit);
        Assert.Equal("Zugführer", current.BosZugfuehrer);
        Assert.Equal("Florian 1", current.BosFunkrufname);
        Assert.Equal("Absicherung", current.BosAufgabenteilung);
        Assert.True(current.BosAbschnittAbgestimmt);
        Assert.True(current.BosRessourcenBesprochen);
        Assert.Equal("Alter Kontakt", current.Vermisste[0].PolizeiKontaktName);
    }

    [Fact]
    public async Task ImportRuntimeSnapshotAsync_MigratesLegacyPersonContactsToEinsatz()
    {
        var einsatz = new EinsatzData
        {
            Vermisste =
            [
                new VermisstenInfo
                {
                    BosZugfuehrer = "Zugführer",
                    BosRessourcenBesprochen = true
                }
            ]
        };
        var service = new EinsatzService();

        await service.ImportRuntimeSnapshotAsync(new EinsatzRuntimeSnapshot { CurrentEinsatz = einsatz });

        Assert.True(service.CurrentEinsatz.EinsatzkontakteMigriert);
        Assert.Equal("Zugführer", service.CurrentEinsatz.BosZugfuehrer);
        Assert.True(service.CurrentEinsatz.BosRessourcenBesprochen);
    }
}

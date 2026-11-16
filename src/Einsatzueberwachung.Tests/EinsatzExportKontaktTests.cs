using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Merge;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

public class EinsatzExportKontaktTests
{
    [Fact]
    public async Task BuildExportPacketAsync_IncludesEinsatzContacts()
    {
        var einsatzService = new EinsatzService();
        await einsatzService.StartEinsatzAsync(new EinsatzData
        {
            PolizeiKontaktName = "Einsatzkontakt",
            PolizeiTelefon = "112",
            BosEinheit = "BOS 1",
            BosZugfuehrer = "Zugführer"
        });
        var exporter = CreateExporter(einsatzService);

        var packet = await exporter.BuildExportPacketAsync(
            Array.Empty<string>(),
            "Test",
            new EinsatzExportOptions { IncludeMapMarkers = false });

        Assert.Equal("Einsatzkontakt", packet.Einsatzkontakte?.PolizeiKontaktName);
        Assert.Equal("112", packet.Einsatzkontakte?.PolizeiTelefon);
        Assert.Equal("BOS 1", packet.Einsatzkontakte?.BosEinheit);
        Assert.Equal("Zugführer", packet.Einsatzkontakte?.BosZugfuehrer);
        using var json = JsonDocument.Parse(exporter.Serialize(packet));
        Assert.Equal(
            "Einsatzkontakt",
            json.RootElement.GetProperty("Einsatzkontakte").GetProperty("PolizeiKontaktName").GetString());
    }

    [Fact]
    public async Task BuildExportPacketFromArchiveAsync_IncludesEinsatzContacts()
    {
        var exporter = CreateExporter(new EinsatzService());
        var archived = new ArchivedEinsatz
        {
            PolizeiKontaktName = "Archivkontakt",
            BosEinheit = "Feuerwehr"
        };

        var packet = await exporter.BuildExportPacketFromArchiveAsync(
            archived,
            Array.Empty<string>(),
            new EinsatzExportOptions { IncludeMapMarkers = false });

        Assert.Equal("Archivkontakt", packet.Einsatzkontakte?.PolizeiKontaktName);
        Assert.Equal("Feuerwehr", packet.Einsatzkontakte?.BosEinheit);
    }

    private static EinsatzExportService CreateExporter(EinsatzService einsatzService)
    {
        var masterData = EinsatzAbschlussTests.Recorder<IMasterDataService>
            .Create((_, _) => null).Proxy;
        return new EinsatzExportService(einsatzService, masterData);
    }
}

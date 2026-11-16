using ClosedXML.Excel;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

public class ExcelExportKontaktTests
{
    [Fact]
    public async Task ExportEinsatzAsync_NutztEinsatzkontakteUndUebergehtMigriertePersonenfelder()
    {
        var einsatz = new EinsatzData
        {
            EinsatzkontakteMigriert = true,
            PolizeiKontaktName = "Einsatzkontakt",
            PolizeiVermisstenmeldungAufgenommen = true
        };
        einsatz.Vermisste.Add(new VermisstenInfo
        {
            PolizeiKontaktName = "Veralteter Kontakt",
            PolizeiVermisstenmeldungAufgenommen = false
        });

        using var workbook = await Export(einsatz);
        var wsInfo = workbook.Worksheet("Einsatz");
        var contactRow = Enumerable.Range(1, wsInfo.LastRowUsed()!.RowNumber())
            .Single(row => wsInfo.Cell(row, 1).GetString() == "Polizei-Kontakt");
        Assert.Equal("Einsatzkontakt", wsInfo.Cell(contactRow, 2).GetString());

        var wsVermisst = workbook.Worksheet("Vermisste");
        Assert.Equal(string.Empty, wsVermisst.Cell(2, 17).GetString());
    }

    [Fact]
    public async Task ExportEinsatzAsync_BehaltLegacyKontaktAlsFallbackWennNichtMigriert()
    {
                var einsatz = System.Text.Json.JsonSerializer.Deserialize<EinsatzData>("""
                {
                    "VermisstenInfo": {
                        "PolizeiKontaktName": "Legacy-Kontakt",
                        "PolizeiDatenschutzGeklaert": true,
                        "BosAufgabenteilung": "Absicherung"
                    }
                }
                """)!;

        using var workbook = await Export(einsatz);
        var wsVermisst = workbook.Worksheet("Vermisste");
        Assert.Equal("Legacy-Kontakt", wsVermisst.Cell(2, 17).GetString());
        Assert.Equal("Ja", wsVermisst.Cell(2, 24).GetString());
        Assert.Equal("Absicherung", wsVermisst.Cell(2, 28).GetString());
    }

    private static async Task<XLWorkbook> Export(EinsatzData einsatz)
    {
        var masterData = EinsatzAbschlussTests.Recorder<IMasterDataService>
            .Create((_, _) => null).Proxy;
        var exporter = new ExcelExportService(masterData);
        var bytes = await exporter.ExportEinsatzAsync(einsatz, [], []);
        return new XLWorkbook(new MemoryStream(bytes));
    }
}

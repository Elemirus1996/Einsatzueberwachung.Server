using ClosedXML.Excel;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

public sealed class ExcelOrganisationTests
{
    [Fact]
    public async Task ExportStammdaten_IncludesOrganisationForPeopleAndDogs()
    {
        var store = CreateStore(
            [new PersonalEntry { Vorname = "Anna", Nachname = "Muster", Organisation = "RHS Nord" }],
            [new DogEntry { Name = "Rex", Rasse = "Mischling", Organisation = "RHS Süd" }]);
        var excel = new ExcelExportService(store.Recorder.Proxy);

        using var workbook = new XLWorkbook(new MemoryStream(await excel.ExportStammdatenAsync()));

        var personal = workbook.Worksheet("Personal");
        Assert.Equal("Organisation", personal.Cell(1, 7).GetString());
        Assert.Equal("RHS Nord", personal.Cell(2, 7).GetString());

        var dogs = workbook.Worksheet("Hunde");
        Assert.Equal("Organisation", dogs.Cell(1, 8).GetString());
        Assert.Equal("RHS Süd", dogs.Cell(2, 8).GetString());
    }

    [Fact]
    public async Task ImportStammdaten_RestoresOrganisationFromOptionalColumns()
    {
        using var workbook = new XLWorkbook();
        var personalSheet = workbook.Worksheets.Add("Personal");
        personalSheet.Cell(1, 1).Value = "Vorname";
        personalSheet.Cell(1, 2).Value = "Nachname";
        personalSheet.Cell(1, 3).Value = "Divera Benutzer-ID";
        personalSheet.Cell(1, 4).Value = "Qualifikationen";
        personalSheet.Cell(1, 5).Value = "Notizen";
        personalSheet.Cell(1, 6).Value = "Aktiv";
        personalSheet.Cell(1, 7).Value = "Organisation";
        personalSheet.Cell(2, 1).Value = "Anna";
        personalSheet.Cell(2, 2).Value = "Muster";
        personalSheet.Cell(2, 6).Value = "Ja";
        personalSheet.Cell(2, 7).Value = "RHS Nord";

        var dogSheet = workbook.Worksheets.Add("Hunde");
        dogSheet.Cell(1, 1).Value = "Name";
        dogSheet.Cell(1, 2).Value = "Rasse";
        dogSheet.Cell(1, 3).Value = "Alter";
        dogSheet.Cell(1, 4).Value = "Spezialisierungen";
        dogSheet.Cell(1, 5).Value = "Hundeführer";
        dogSheet.Cell(1, 6).Value = "Notizen";
        dogSheet.Cell(1, 7).Value = "Aktiv";
        dogSheet.Cell(1, 8).Value = "Organisation";
        dogSheet.Cell(2, 1).Value = "Rex";
        dogSheet.Cell(2, 2).Value = "Mischling";
        dogSheet.Cell(2, 7).Value = "Ja";
        dogSheet.Cell(2, 8).Value = "RHS Süd";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var store = CreateStore();
        var result = await new ExcelExportService(store.Recorder.Proxy).ImportStammdatenAsync(stream.ToArray());

        Assert.True(result.Success);
        Assert.Equal("RHS Nord", Assert.Single(store.Personal).Organisation);
        Assert.Equal("RHS Süd", Assert.Single(store.Dogs).Organisation);
    }

    private static Store CreateStore(List<PersonalEntry>? personal = null, List<DogEntry>? dogs = null)
    {
        personal ??= new List<PersonalEntry>();
        dogs ??= new List<DogEntry>();
        var drones = new List<DroneEntry>();
        var recorder = EinsatzAbschlussTests.Recorder<IMasterDataService>.Create((method, args) =>
        {
            switch (method.Name)
            {
                case nameof(IMasterDataService.GetPersonalListAsync):
                    return Task.FromResult(personal.ToList());
                case nameof(IMasterDataService.GetDogListAsync):
                    return Task.FromResult(dogs.ToList());
                case nameof(IMasterDataService.GetDroneListAsync):
                    return Task.FromResult(drones.ToList());
                case nameof(IMasterDataService.AddPersonalAsync):
                    personal.Add((PersonalEntry)args![0]!);
                    return Task.CompletedTask;
                case nameof(IMasterDataService.AddDogAsync):
                    dogs.Add((DogEntry)args![0]!);
                    return Task.CompletedTask;
                case nameof(IMasterDataService.AddDroneAsync):
                    drones.Add((DroneEntry)args![0]!);
                    return Task.CompletedTask;
                default:
                    throw new NotSupportedException(method.Name);
            }
        });

        return new Store(personal, dogs, recorder);
    }

    private sealed record Store(
        List<PersonalEntry> Personal,
        List<DogEntry> Dogs,
        EinsatzAbschlussTests.Recorder<IMasterDataService> Recorder);
}

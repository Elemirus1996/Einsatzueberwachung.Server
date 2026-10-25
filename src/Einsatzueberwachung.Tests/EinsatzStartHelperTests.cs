using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Divera;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

// ------------------------------------------------------------
// Gemeinsame Startlogik der großen Startseite und der EL-Startseite (/el/start)
// ------------------------------------------------------------
public class EinsatzStartHelperTests
{
    [Fact]
    public void DiveraAlarm_MitKoordinate_FuelltOrtKarteUndLage()
    {
        var model = new EinsatzData();
        var alarm = new DiveraAlarm
        {
            Id = 42, Title = "Vermisste Person", Address = "Waldweg 1, Testdorf", Caller = "Polizei",
            Lat = 51.123456, Lng = 7.654321, Text = "Person seit 14 Uhr vermisst", Remark = "Demenz",
            Date = new DateTime(2026, 10, 25, 20, 15, 0),
        };

        var hasCoordinates = EinsatzStartHelper.ApplyDiveraAlarm(model, alarm);

        Assert.True(hasCoordinates);
        Assert.Equal("Waldweg 1, Testdorf", model.Einsatzort);
        Assert.Equal("51.123456,7.654321", model.MapAddress);
        Assert.Equal((51.123456, 7.654321), model.ElwPosition);
        Assert.Equal("Vermisste Person", model.Stichwort);
        Assert.Equal("Polizei", model.Alarmiert);
        Assert.Equal("D-42", model.EinsatzNummer);
        Assert.True(model.IstEinsatz);
        Assert.Contains("Person seit 14 Uhr vermisst", model.ExportPfad);
        Assert.Contains("Hinweis: Demenz", model.ExportPfad);
        Assert.Equal(alarm.Date, model.AlarmierungsZeit);
    }

    [Fact]
    public void DiveraAlarm_OhneKoordinate_AdresseAlsKarte_FremdnummerBevorzugt()
    {
        var model = new EinsatzData();
        var hasCoordinates = EinsatzStartHelper.ApplyDiveraAlarm(model,
            new DiveraAlarm { Id = 7, ForeignId = "E-2026-117", Title = "Fläche", Address = "Sportplatz Nord" });

        Assert.False(hasCoordinates);
        Assert.Equal("Sportplatz Nord", model.MapAddress);
        Assert.Equal("E-2026-117", model.EinsatzNummer);
        Assert.Equal("Fläche", model.ExportPfad);
    }

    [Fact]
    public void Handystandort_WirdAlsKoordinateUndElwPositionUebernommen()
    {
        var model = new EinsatzData();
        EinsatzStartHelper.ApplyCoordinates(model, 51.5, 7.25);
        Assert.Equal("51.500000,7.250000", model.MapAddress);
        Assert.Equal((51.5, 7.25), model.ElwPosition);
    }

    [Fact]
    public void Pflichtangaben_OrtUndSzenario()
    {
        var model = new EinsatzData { Einsatzort = "", Szenario = EinsatzSzenarioType.Flaeche };
        Assert.Equal("Bitte Einsatzort ausfüllen.", EinsatzStartHelper.Validate(model));

        model.Einsatzort = "Waldparkplatz";
        model.Szenario = EinsatzSzenarioType.Unbestimmt;
        Assert.Equal("Bitte ein Szenario auswählen.", EinsatzStartHelper.Validate(model));

        model.Szenario = EinsatzSzenarioType.Mantrailer;
        Assert.Null(EinsatzStartHelper.Validate(model));
    }

    [Fact]
    public void Staffeldaten_WerdenUebernommen_LeererNameUeberschreibtNicht()
    {
        var model = new EinsatzData { StaffelName = "Vorhanden" };
        EinsatzStartHelper.ApplyStaffel(model, new StaffelSettings { StaffelName = "", StaffelTelefon = "0123" });
        Assert.Equal("Vorhanden", model.StaffelName);
        Assert.Equal("0123", model.StaffelTelefon);
    }
}

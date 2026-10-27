using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>Truemmer-Upload ohne Umweg ueber die eigene API (1.11.6) – Pruefungen greifen vor dem Speichern</summary>
public class TruemmerKartenSpeicherTests
{
    private static readonly TruemmerKartenSpeicher Speicher = new(null!);

    [Theory]
    [InlineData("lage.gif", "Nur JPG, PNG oder WEBP erlaubt.")]
    [InlineData("lage.png", "Bildinhalt passt nicht zur Dateiendung.")]
    public async Task UngueltigeDateienWerdenAbgelehnt(string name, string erwartet)
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 };

        var (karte, fehler) = await Speicher.SpeichernAsync(jpeg, name, null);

        Assert.Null(karte);
        Assert.Equal(erwartet, fehler);
    }

    [Fact]
    public async Task LeereUndZuGrosseDateienWerdenAbgelehnt()
    {
        Assert.Equal("Keine Datei übermittelt.", (await Speicher.SpeichernAsync([], "a.jpg", null)).Fehler);
        Assert.StartsWith("Datei zu groß", (await Speicher.SpeichernAsync(new byte[TruemmerKartenSpeicher.MaxImageBytes + 1], "a.jpg", null)).Fehler);
    }

    [Fact]
    public async Task KaputtesBildMitPassendenMagicBytesWirdAbgelehnt()
    {
        var (karte, fehler) = await Speicher.SpeichernAsync(new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, "a.jpg", null);

        Assert.Null(karte);
        Assert.NotNull(fehler);
    }
}

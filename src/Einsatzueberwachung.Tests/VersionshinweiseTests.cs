using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>Versionshinweise unter Einstellungen → Updates</summary>
public sealed class VersionshinweiseTests
{
    [Fact]
    public void Zerlegen_UeberschriftenPunkteAbsaetzeFettUndCode()
    {
        var bloecke = Versionshinweise.Zerlegen("## Neu\r\n\r\n### Bericht\n- **Seite 1** mit `Vorgangsnummer`\nText <script>x</script>");

        Assert.Equal(
            [Versionshinweise.BlockArt.Ueberschrift, Versionshinweise.BlockArt.Unterueberschrift, Versionshinweise.BlockArt.Punkt, Versionshinweise.BlockArt.Absatz],
            bloecke.Select(b => b.Art));
        Assert.Equal("Neu", Assert.Single(bloecke[0].Teile).Text);
        var punkt = bloecke[2].Teile;
        Assert.Equal(new Versionshinweise.Teil("Seite 1", true, false), punkt[0]);
        Assert.Equal(new Versionshinweise.Teil(" mit ", false, false), punkt[1]);
        Assert.Equal(new Versionshinweise.Teil("Vorgangsnummer", false, true), punkt[2]);
        // HTML bleibt Text, die Seite gibt es maskiert aus
        Assert.Equal("Text <script>x</script>", Assert.Single(bloecke[3].Teile).Text);
    }

    [Fact]
    public void Zerlegen_OhneSchluss_BleibtText()
    {
        var teile = Versionshinweise.Inline("a ** b ` c");
        Assert.Equal("a ** b ` c", Assert.Single(teile).Text);
        Assert.Empty(Versionshinweise.Zerlegen(null));
    }
}

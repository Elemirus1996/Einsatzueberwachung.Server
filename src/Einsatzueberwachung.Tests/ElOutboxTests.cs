using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

public class ElOutboxTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 10, 26, 16, 30, 0, TimeSpan.Zero);
    private static readonly DateTime JetztLokal = new(2026, 10, 26, 17, 30, 0);

    private static ElOutboxEntry Eintrag(string kind = "funk", string text = "Lage ruhig", string? an = null, DateTimeOffset? erfasst = null) =>
        new("id-1", kind, text, an, erfasst ?? Jetzt);

    [Fact]
    public void Validate_LehntUngueltigeEintraegeAb()
    {
        Assert.Null(ElOutboxService.Validate(Eintrag()));
        Assert.NotNull(ElOutboxService.Validate(Eintrag(kind: "loeschen")));
        Assert.NotNull(ElOutboxService.Validate(Eintrag(text: "  ")));
        Assert.NotNull(ElOutboxService.Validate(Eintrag(text: new string('x', ElOutboxService.MaxTextLength + 1))));
        Assert.NotNull(ElOutboxService.Validate(Eintrag(an: new string('x', ElOutboxService.MaxRecipientLength + 1))));
        Assert.NotNull(ElOutboxService.Validate(Eintrag() with { Id = "" }));
    }

    [Fact]
    public void TryClaim_DoppeltGesendeterEintragWirdNurEinmalVerarbeitet()
    {
        var outbox = new ElOutboxService();

        Assert.True(outbox.TryClaim("a", Jetzt));
        Assert.False(outbox.TryClaim("a", Jetzt.AddMinutes(1)));

        outbox.Release("a"); // Verarbeitung fehlgeschlagen -> Geraet darf erneut senden
        Assert.True(outbox.TryClaim("a", Jetzt.AddMinutes(2)));
    }

    [Fact]
    public void BuildText_SofortZugestelltOhneVermerk_FunkMitEmpfaenger()
    {
        Assert.Equal("@Rex Lage ruhig", ElOutboxService.BuildText(Eintrag(an: "Rex"), Jetzt, JetztLokal));
        Assert.Equal("Lage ruhig", ElOutboxService.BuildText(Eintrag(kind: "notiz", an: "Rex"), Jetzt, JetztLokal));
    }

    [Fact]
    public void BuildText_NachgesendetVermerktErfassungszeitInOrtszeit()
    {
        var text = ElOutboxService.BuildText(Eintrag(erfasst: Jetzt.AddMinutes(-12)), Jetzt, JetztLokal);

        Assert.Equal("Lage ruhig [im Funkloch erfasst 17:18, nachgesendet]", text);
    }

    [Fact]
    public void BuildText_FalscheGeraeteuhrWirdBegrenzt()
    {
        // Zukunft -> wie sofort zugestellt
        Assert.Equal("Lage ruhig", ElOutboxService.BuildText(Eintrag(erfasst: Jetzt.AddHours(3)), Jetzt, JetztLokal));
        // Uralt -> hoechstens 24 h zurueck
        Assert.EndsWith("erfasst 17:30, nachgesendet]", ElOutboxService.BuildText(Eintrag(erfasst: Jetzt.AddDays(-5)), Jetzt, JetztLokal));
    }
}

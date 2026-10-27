using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Server.Services;

namespace Einsatzueberwachung.Tests;

/// <summary>Knopf „Jetzt erneuern“ beim Zertifikatshinweis (1.11.6)</summary>
public class ZertifikatErneuerungTests
{
    private sealed class Audit : IAuditLogService
    {
        public List<(string Aktion, string Details)> Eintraege { get; } = new();
        public void Log(string aktion, string details, string? teamName = null) => Eintraege.Add((aktion, details));
        public IReadOnlyList<AuditLogEntry> Entries => [];
        public void Clear() => Eintraege.Clear();
        public Task<List<AuditLogEntry>> GetEntriesAsync(int maxCount = 500) => Task.FromResult(new List<AuditLogEntry>());
    }

    [Theory]
    [InlineData(0, "ergebnis=erneuert\nSuccessfully received certificate.", ZertifikatErneuerungsErgebnis.Erneuert)]
    [InlineData(0, "ergebnis=nicht_faellig\nnot yet due", ZertifikatErneuerungsErgebnis.NichtFaellig)]
    [InlineData(1, "ergebnis=fehler\nChallenge failed", ZertifikatErneuerungsErgebnis.Fehler)]
    [InlineData(0, "", ZertifikatErneuerungsErgebnis.Fehler)]
    [InlineData(1, "sudo: a password is required", ZertifikatErneuerungsErgebnis.Fehler)]
    public void AusgabeDesHilfsprogrammsWirdAusgewertet(int code, string ausgabe, ZertifikatErneuerungsErgebnis erwartet)
    {
        var antwort = ZertifikatErneuerung.Auswerten(code, ausgabe);

        Assert.Equal(erwartet, antwort.Ergebnis);
        Assert.DoesNotContain("ergebnis=", antwort.Ausgabe);
    }

    [Fact]
    public async Task OhneHilfsprogrammHinweisAufEinrichtung()
    {
        var audit = new Audit();
        var dienst = new ZertifikatErneuerung(_ => throw new InvalidOperationException("darf nicht laufen"), () => false, audit);

        var antwort = await dienst.ErneuernAsync();

        Assert.False(antwort.Erfolgreich);
        Assert.Contains("setup-zertifikat.sh", antwort.Text);
        Assert.Empty(audit.Eintraege);
    }

    [Fact]
    public async Task GleichzeitigesDrueckenStartetCertbotNurEinmal()
    {
        var audit = new Audit();
        var freigabe = new TaskCompletionSource<(int, string)>();
        var aufrufe = 0;
        var dienst = new ZertifikatErneuerung(_ => { aufrufe++; return freigabe.Task; }, () => true, audit);

        var erster = dienst.ErneuernAsync("Einsatzleitung · Tablet");
        Assert.True(dienst.Laeuft);
        var zweiter = await dienst.ErneuernAsync();
        freigabe.SetResult((0, "ergebnis=erneuert"));
        var antwort = await erster;

        Assert.Equal(1, aufrufe);
        Assert.Contains("läuft bereits", zweiter.Text);
        Assert.Equal(ZertifikatErneuerungsErgebnis.Erneuert, antwort.Ergebnis);
        Assert.False(dienst.Laeuft);
        Assert.Contains(audit.Eintraege, e => e.Aktion == "Zertifikat erneuern" && e.Details.Contains("Tablet"));
    }
}

using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Data;
using Einsatzueberwachung.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Einsatzueberwachung.Tests;

/// <summary>Loeschfristen je Staffel (#151)</summary>
public sealed class LoeschfristenTests : IDisposable
{
    private static readonly DateTime Jetzt = new(2026, 10, 31, 12, 0, 0);
    private readonly string _berichte = Path.Combine(Path.GetTempPath(), "eu-loesch-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _verbindung = new("DataSource=:memory:");

    public LoeschfristenTests()
    {
        Directory.CreateDirectory(_berichte);
        _verbindung.Open();
        using var db = Db();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _verbindung.Dispose();
        Directory.Delete(_berichte, recursive: true);
    }

    private RuntimeDbContext Db() => new(new DbContextOptionsBuilder<RuntimeDbContext>().UseSqlite(_verbindung).Options);

    private sealed class DbFabrik(Func<RuntimeDbContext> neu) : IDbContextFactory<RuntimeDbContext>
    {
        public RuntimeDbContext CreateDbContext() => neu();
    }

    private static ArchivedEinsatz Archiviert(int tageSeitEnde, bool einsatz = true, string nummer = "E-1") => new()
    {
        EinsatzNummer = nummer,
        IstEinsatz = einsatz,
        EinsatzDatum = Jetzt.AddDays(-tageSeitEnde).AddHours(-3),
        EinsatzEnde = Jetzt.AddDays(-tageSeitEnde),
        TrackSnapshots = [new TeamTrackSnapshot { TeamId = "t1", Points = [new TrackPoint { Latitude = 49.3, Longitude = 8.4 }] }],
        CompletedSearches = [new CompletedSearch { TeamId = "t1", Tracks = [new TeamTrackSnapshot()] }],
        GlobalNotesEntries = [new GlobalNotesEntry { Text = "Hund zeigt an bei Hausnummer 12" }],
        Vermisste = [new VermisstenInfo { Vorname = "Erna", Nachname = "Muster" }]
    };

    [Fact]
    public void Faellig_NachFrist_NichtVorher_OhneFristNie_UebungEigeneFrist()
    {
        var s = new AppSettings { LoeschfristLaufwegeTage = 30, LoeschfristVermissteTage = 90 };

        Assert.Empty(Loeschfristen.Faellig(Archiviert(29), s, Jetzt));
        Assert.Equal([Loeschfristen.Laufwege], Loeschfristen.Faellig(Archiviert(30), s, Jetzt));
        Assert.Equal([Loeschfristen.Laufwege, Loeschfristen.Vermisste], Loeschfristen.Faellig(Archiviert(400), s, Jetzt)); // Notizen: nie
        Assert.Empty(Loeschfristen.Faellig(Archiviert(10_000), new AppSettings(), Jetzt));                                     // ab Werk

        s.LoeschfristUebungenTage = 7;
        Assert.Equal(Loeschfristen.Arten, Loeschfristen.Faellig(Archiviert(8, einsatz: false), s, Jetzt));
        Assert.Empty(Loeschfristen.Faellig(Archiviert(8), s, Jetzt));
    }

    [Fact]
    public async Task Ausfuehren_LoeschtNurFaelliges_EinmalMitVermerk_PdfWeg_AuditOhneInhalt()
    {
        var alt = Archiviert(40, nummer: "E-7");
        var neu = Archiviert(5, nummer: "E-8");
        var gespeichert = new List<ArchivedEinsatz>();
        var archiv = EinsatzAbschlussTests.Recorder<IArchivService>.Create((m, args) => m.Name switch
        {
            nameof(IArchivService.GetAllArchivedAsync) => Task.FromResult(new List<ArchivedEinsatz> { alt, neu }),
            nameof(IArchivService.UpdateArchivedEinsatzAsync) => Speichern((ArchivedEinsatz)args![0]!),
            _ => null
        });
        Task Speichern(ArchivedEinsatz a) { gespeichert.Add(a); return Task.CompletedTask; }
        var einstellungen = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) =>
            m.Name == nameof(ISettingsService.GetAppSettingsAsync) ? Task.FromResult(new AppSettings { LoeschfristLaufwegeTage = 30, LoeschfristNotizenTage = 30 }) : null);
        var details = new List<string>();
        var audit = EinsatzAbschlussTests.Recorder<IAuditLogService>.Create((m, args) => { details.Add((string)args![1]!); return null; });
        var pdf = Path.Combine(_berichte, $"Einsatzbericht_E-7_{alt.EinsatzDatum:yyyyMMdd}.pdf");
        await File.WriteAllTextAsync(pdf, "pdf");
        var dienst = new LoeschfristenService(archiv.Proxy, new EinsatzService(), einstellungen.Proxy, audit.Proxy, null, _berichte);

        var ergebnis = await dienst.AusfuehrenAsync(Jetzt);

        Assert.Equal(1, ergebnis.Einsaetze);
        Assert.Same(alt, Assert.Single(gespeichert));
        Assert.Empty(alt.TrackSnapshots);
        Assert.All(alt.CompletedSearches!, s => Assert.Empty(s.Tracks));
        Assert.Empty(alt.GlobalNotesEntries);
        Assert.Single(alt.Vermisste); // keine Frist
        Assert.Equal(Jetzt, alt.Geloescht![Loeschfristen.Laufwege]);
        Assert.Single(neu.TrackSnapshots);
        Assert.False(File.Exists(pdf));
        var eintrag = Assert.Single(details);
        Assert.Contains("E-7", eintrag);
        Assert.DoesNotContain("Hausnummer", eintrag);

        // Zweiter Lauf: schon geloescht, nichts mehr zu tun
        Assert.Equal(0, (await dienst.AusfuehrenAsync(Jetzt)).Einsaetze);
        // Rueckfrage vor dem Speichern: Vermisste nach 1 Tag betraefe beide Einsaetze sofort
        Assert.Equal(2, await dienst.BetroffenAsync(new AppSettings { LoeschfristVermissteTage = 1 }, Jetzt));
    }

    [Fact]
    public async Task Funk_AlteGeloescht_LaufenderEinsatzBleibt()
    {
        var einsatz = new EinsatzService();
        await einsatz.StartEinsatzAsync(new EinsatzData { Einsatzort = "Testwald", IstEinsatz = true, EinsatzDatum = Ortszeit.Jetzt.AddDays(-60) });
        await using (var db = Db())
        {
            db.RadioMessages.Add(new RadioMessageEntity { Id = "alt", TimestampUtc = DateTime.UtcNow.AddDays(-100), Replies = [new RadioReplyEntity { Id = "a1", MessageId = "alt" }] });
            db.RadioMessages.Add(new RadioMessageEntity { Id = "laufend", TimestampUtc = DateTime.UtcNow.AddDays(-50) }); // im laufenden Einsatz
            db.RadioMessages.Add(new RadioMessageEntity { Id = "neu", TimestampUtc = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        var archiv = EinsatzAbschlussTests.Recorder<IArchivService>.Create((m, _) =>
            m.Name == nameof(IArchivService.GetAllArchivedAsync) ? Task.FromResult(new List<ArchivedEinsatz>()) : null);
        var einstellungen = EinsatzAbschlussTests.Recorder<ISettingsService>.Create((m, _) =>
            m.Name == nameof(ISettingsService.GetAppSettingsAsync) ? Task.FromResult(new AppSettings { LoeschfristNotizenTage = 30 }) : null);
        var dienst = new LoeschfristenService(archiv.Proxy, einsatz, einstellungen.Proxy, null, new DbFabrik(Db), _berichte);

        Assert.Equal(1, (await dienst.AusfuehrenAsync(Ortszeit.Jetzt)).Funksprueche);

        await using var pruefen = Db();
        Assert.Equal(["laufend", "neu"], pruefen.RadioMessages.Select(m => m.Id).OrderBy(i => i).ToArray());
        Assert.Empty(pruefen.RadioReplies);
    }

    [Fact]
    public void Datenschutz_FristenEingesetzt_NeueVersionBeiGeaenderterFrist()
    {
        var s = new AppSettings { AppDatenschutzText = "Laufwege: {frist.laufwege}. Übungen: {frist.uebungen}." };
        var ohne = AppDatenschutz.Version(s);
        Assert.Contains("keine automatische Löschfrist", AppDatenschutz.Text(s));
        Assert.Contains("wie bei Einsätzen", AppDatenschutz.Text(s));

        s.LoeschfristLaufwegeTage = 30;
        Assert.Contains("30 Tage nach Ende des Einsatzes", AppDatenschutz.Text(s));
        Assert.NotEqual(ohne, AppDatenschutz.Version(s));
        Assert.Contains("{frist.laufwege}", AppDatenschutzAntwort.Vorlage);
    }
}

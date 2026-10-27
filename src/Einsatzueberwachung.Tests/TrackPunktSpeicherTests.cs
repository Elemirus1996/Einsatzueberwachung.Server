using System.Text.Json;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Einsatzueberwachung.Tests;

/// <summary>Lange Tracks ueberstehen einen Neustart vollstaendig (#89, 1.11.7)</summary>
public sealed class TrackPunktSpeicherTests : IDisposable
{
    private readonly SqliteConnection _verbindung = new("DataSource=:memory:");
    private static readonly DateTime Start = new(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc);

    public TrackPunktSpeicherTests() => _verbindung.Open();

    public void Dispose() => _verbindung.Dispose();

    private RuntimeDbContext Db() =>
        new(new DbContextOptionsBuilder<RuntimeDbContext>().UseSqlite(_verbindung).Options);

    private static TrackPunktSpeicher<CollarLocation> Speicher() =>
        new("collar", p => p.Timestamp, p => (p.Latitude, p.Longitude), new JsonSerializerOptions());

    private static CollarLocation Punkt(int i) =>
        new() { CollarId = "c1", Latitude = 49.3 + i * 1e-5, Longitude = 8.43, Timestamp = Start.AddSeconds(5 * i) };

    private static Dictionary<string, IReadOnlyList<CollarLocation>> Verlauf(string key, IEnumerable<CollarLocation> punkte) =>
        new() { [key] = punkte.ToList() };

    private async Task<RuntimeDbContext> AlteDatenbankOhneTabelleAsync()
    {
        var db = Db();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE runtime_track_points");
        await TrackPunktSpeicher<CollarLocation>.TabelleAnlegenAsync(db, default);
        return db;
    }

    [Fact]
    public async Task LangerTrack_WirdOhneObergrenzeUndInZeitfolgeWiederhergestellt()
    {
        await using var db = await AlteDatenbankOhneTabelleAsync();
        var speicher = Speicher();
        var punkte = Enumerable.Range(0, 5000).Select(Punkt).ToList(); // ca. 7 h bei 5 s – frueher nach 2000 gekappt

        await speicher.AbgleichenAsync(db, Verlauf("c1", punkte), default);
        // Nachgesendeter Punkt aus dem Funkloch landet mitten im Verlauf
        var nachgesendet = new CollarLocation { CollarId = "c1", Latitude = 1, Longitude = 2, Timestamp = Start.AddSeconds(12) };
        punkte.Insert(3, nachgesendet);
        await speicher.AbgleichenAsync(db, Verlauf("c1", punkte), default);

        Assert.Equal(5001, await db.TrackPoints.CountAsync());

        var geladen = (await Speicher().LadenAsync(Db(), default))["c1"];
        Assert.Equal(5001, geladen.Count);
        Assert.Equal(punkte.Select(p => p.Timestamp), geladen.Select(p => p.Timestamp));
    }

    [Fact]
    public async Task NeueSucheErsetztVerlauf_GeleerterVerlaufWirdGeloescht()
    {
        await using var db = await AlteDatenbankOhneTabelleAsync();
        var speicher = Speicher();
        await speicher.AbgleichenAsync(db, Verlauf("c1", Enumerable.Range(0, 10).Select(Punkt)), default);

        // Neue Suche: Verlauf wurde geleert und neu begonnen
        await speicher.AbgleichenAsync(db, Verlauf("c1", Enumerable.Range(100, 3).Select(Punkt)), default);
        Assert.Equal(3, await db.TrackPoints.CountAsync());

        await speicher.AbgleichenAsync(db, new Dictionary<string, IReadOnlyList<CollarLocation>>(), default);
        Assert.Equal(0, await db.TrackPoints.CountAsync());
    }

    [Fact]
    public async Task NachLadenWerdenNurNeuePunkteGeschrieben()
    {
        await using var db = await AlteDatenbankOhneTabelleAsync();
        await Speicher().AbgleichenAsync(db, Verlauf("c1", Enumerable.Range(0, 20).Select(Punkt)), default);

        // Neustart: neuer Speicher laedt, Verlauf waechst weiter
        var neu = Speicher();
        var geladen = (await neu.LadenAsync(db, default))["c1"];
        Assert.Contains("c1", neu.GespeicherteSchluessel);
        geladen.AddRange(Enumerable.Range(20, 5).Select(Punkt));
        await neu.AbgleichenAsync(db, Verlauf("c1", geladen), default);

        Assert.Equal(25, await db.TrackPoints.CountAsync());
    }
}

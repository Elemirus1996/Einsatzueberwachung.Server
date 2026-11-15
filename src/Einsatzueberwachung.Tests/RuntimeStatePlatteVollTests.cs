using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Services;
using Einsatzueberwachung.Server.Data;
using Einsatzueberwachung.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Einsatzueberwachung.Tests;

/// <summary>Platte voll (#181): Datenbankfehler beim Start beenden nicht mehr den Server, sondern warnen und werden spaeter erneut versucht</summary>
public sealed class RuntimeStatePlatteVollTests : IDisposable
{
    private readonly SqliteConnection _verbindung = new("DataSource=:memory:");

    public RuntimeStatePlatteVollTests() => _verbindung.Open();

    public void Dispose() => _verbindung.Dispose();

    /// <summary>Wirft wie eine volle Platte, solange <see cref="Kaputt"/> gesetzt ist</summary>
    private sealed class Fabrik(SqliteConnection verbindung) : IDbContextFactory<RuntimeDbContext>
    {
        public bool Kaputt { get; set; } = true;

        public RuntimeDbContext CreateDbContext() => Kaputt
            ? throw new SqliteException("SQLite Error 10: 'disk I/O error'.", 10)
            : new(new DbContextOptionsBuilder<RuntimeDbContext>().UseSqlite(verbindung).Options);
    }

    [Fact]
    public async Task DatenbankFehler_WarntEinmal_UndErholtSich()
    {
        var fabrik = new Fabrik(_verbindung);
        var hinzu = new List<WarningEntry>();
        var entfernt = new List<string>();
        var warnungen = EinsatzAbschlussTests.Recorder<IWarningService>.Create((m, args) =>
        {
            if (m.Name == nameof(IWarningService.AddWarning)) hinzu.Add((WarningEntry)args![0]!);
            if (m.Name == nameof(IWarningService.DismissWarning)) entfernt.Add((string)args![0]!);
            return null;
        });
        var einsatz = new EinsatzService();
        var dienst = new RuntimeStatePersistenceService(fabrik, einsatz, new CollarTrackingService(einsatz),
            NullLogger<RuntimeStatePersistenceService>.Instance, warnungen.Proxy);

        Assert.False(await dienst.DatenbankStartenAsync(wiederherstellen: true, CancellationToken.None));
        Assert.False(await dienst.DatenbankStartenAsync(wiederherstellen: true, CancellationToken.None));
        Assert.False(dienst.DatenbankBereit);
        var warnung = Assert.Single(hinzu); // nicht bei jedem Versuch erneut
        Assert.Equal(WarningLevel.Critical, warnung.Level);
        Assert.Equal(WarningRuleDefinition.Sources.ServerStorage, warnung.Source);

        fabrik.Kaputt = false;
        Assert.True(await dienst.DatenbankStartenAsync(wiederherstellen: true, CancellationToken.None));
        Assert.True(dienst.DatenbankBereit);
        Assert.Equal([warnung.Id], entfernt);

        // Erneuter Ausfall nach Erholung warnt wieder
        fabrik.Kaputt = true;
        Assert.False(await dienst.DatenbankStartenAsync(wiederherstellen: false, CancellationToken.None));
        Assert.Equal(2, hinzu.Count);
    }
}

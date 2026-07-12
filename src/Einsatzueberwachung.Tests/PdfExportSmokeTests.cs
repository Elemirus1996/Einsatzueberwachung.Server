using System.Text;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Tests;

// ------------------------------------------------------------
// PDF-Export — Smoketest
// Faengt die Klasse Bugs ab, die in 1.7.5/1.7.6/1.7.8 auf prod
// gelandet sind: QuestPDF + libSkiaSharp.so muessen auf Linux
// laufen, sonst kommt vom Endpoint nur 0 Bytes oder 502 zurueck.
// ------------------------------------------------------------
public class PdfExportSmokeTests
{
    private static EinsatzData MinimalEinsatz() => new()
    {
        Einsatzort = "Testwald",
        Stichwort = "Vermisstensuche",
        Einsatzleiter = "Max Mustermann",
        Fuehrungsassistent = "Lisa Beispiel",
        EinsatzNummer = "TEST-2026-001",
        Alarmiert = "12:00",
        IstEinsatz = true,
        EinsatzDatum = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Local),
        AlarmierungsZeit = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Local),
        StaffelName = "Rettungshundestaffel Test",
        AnzahlTeams = 1
    };

    private static List<Team> MinimalTeams() => new()
    {
        new Team
        {
            TeamName = "Team 1",
            HundefuehrerName = "Max Mustermann",
            DogName = "Rex"
        }
    };

    private static List<GlobalNotesEntry> MinimalNotes() => new()
    {
        new GlobalNotesEntry
        {
            Text = "Einsatz gestartet",
            Timestamp = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Local),
            Type = GlobalNotesEntryType.System
        }
    };

    [Fact]
    public async Task ExportEinsatzToPdfBytesAsync_LiefertGueltigesPdf()
    {
        var service = new PdfExportService();

        var bytes = await service.ExportEinsatzToPdfBytesAsync(
            MinimalEinsatz(), MinimalTeams(), MinimalNotes());

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 1024,
            $"PDF zu klein ({bytes.Length} Bytes) - QuestPDF/Skia liefert wahrscheinlich nichts Reales.");

        var header = Encoding.ASCII.GetString(bytes, 0, 5);
        Assert.Equal("%PDF-", header);

        var tail = Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 16), Math.Min(16, bytes.Length));
        Assert.Contains("%%EOF", tail);
    }

    [Fact]
    public async Task ExportEinsatzToPdfBytesAsync_OhneTeamsUndNotizen_LiefertTrotzdemPdf()
    {
        var service = new PdfExportService();

        var bytes = await service.ExportEinsatzToPdfBytesAsync(
            MinimalEinsatz(), new List<Team>(), new List<GlobalNotesEntry>());

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 1024);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public async Task ExportEinsatzToPdfBytesAsync_LaeuftZweimalNacheinander()
    {
        // QuestPDF/Skia haelt Native-Handles - zweite Generierung muss auch klappen.
        var service = new PdfExportService();

        var first = await service.ExportEinsatzToPdfBytesAsync(
            MinimalEinsatz(), MinimalTeams(), MinimalNotes());
        var second = await service.ExportEinsatzToPdfBytesAsync(
            MinimalEinsatz(), MinimalTeams(), MinimalNotes());

        Assert.True(first.Length > 1024);
        Assert.True(second.Length > 1024);
    }

    [Fact]
    public async Task ExportEinsatzKarteToPdfBytesAsync_OhneMapRenderer_LiefertGueltigesPdf()
    {
        // mapRenderer=null: kein Netzwerk, kein OSM - trotzdem vollstaendiges 2-seitiges PDF.
        var service = new PdfExportService();
        var einsatz = MinimalEinsatz();
        einsatz.SearchAreas = new List<SearchArea>
        {
            new SearchArea { Name = "Gebiet Nord", AssignedTeamName = "Team 1" }
        };

        var bytes = await service.ExportEinsatzKarteToPdfBytesAsync(
            einsatz, MinimalTeams());

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 1024,
            $"Karte-PDF zu klein ({bytes.Length} Bytes).");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        var tail = Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 16), Math.Min(16, bytes.Length));
        Assert.Contains("%%EOF", tail);
    }

    [Fact]
    public async Task ExportEinsatzToPdfBytesAsync_RendertTracksSchwarz()
    {
        var mapRenderer = new RecordingMapRenderer();
        var service = new PdfExportService(mapRenderer: mapRenderer);
        var einsatz = MinimalEinsatz();
        einsatz.TrackSnapshots = new List<TeamTrackSnapshot>
        {
            new()
            {
                Color = "#e63946",
                Points = new List<TrackPoint>
                {
                    new() { Latitude = 49.0, Longitude = 8.0 },
                    new() { Latitude = 49.1, Longitude = 8.1 }
                }
            }
        };

        await service.ExportEinsatzToPdfBytesAsync(einsatz, MinimalTeams(), MinimalNotes(), includeTracks: true);

        Assert.Equal("#000000", mapRenderer.TrackColor);
    }

    private sealed class RecordingMapRenderer : IStaticMapRenderer
    {
        public string? TrackColor { get; private set; }

        public Task<byte[]?> RenderTrackMapAsync(List<TrackPoint> trackPoints,
            List<(double Latitude, double Longitude)>? searchAreaCoords, string trackColor,
            string? areaColor, int width = 800, int height = 450)
        {
            TrackColor = trackColor;
            return Task.FromResult<byte[]?>(null);
        }

        public Task<byte[]?> RenderCombinedTrackMapAsync(List<TeamTrackSnapshot> tracks,
            (double Latitude, double Longitude)? elwPosition, int width = 1200, int height = 780)
            => Task.FromResult<byte[]?>(null);

        public Task<byte[]?> RenderSearchAreaMapAsync(List<SearchArea> searchAreas,
            (double Latitude, double Longitude)? elwPosition, MapTileType tileType = MapTileType.Streets,
            int width = 1500, int height = 1060)
            => Task.FromResult<byte[]?>(null);

        public Task<byte[]?> RenderSearchAreaMapAsync(List<SearchArea> searchAreas,
            (double Latitude, double Longitude)? elwPosition, MapPrintOptions options,
            List<MapMarker>? markers = null, List<TeamTrackSnapshot>? gpsTracks = null,
            Dictionary<string, List<TeamPhoneLocation>>? phoneTrackHistories = null,
            List<Team>? teams = null, int width = 1500, int height = 1060)
            => Task.FromResult<byte[]?>(null);
    }
}

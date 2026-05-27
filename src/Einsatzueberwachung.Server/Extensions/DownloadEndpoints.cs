using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Services;
using Microsoft.AspNetCore.StaticFiles;
using System.IO.Compression;
using System.Text.Json;

namespace Einsatzueberwachung.Server.Extensions;

internal static class DownloadEndpoints
{
    public static WebApplication MapDownloadEndpoints(this WebApplication app)
    {
        app.MapGet("/downloads/einsatz-bericht.pdf", async (IEinsatzService einsatzService, IPdfExportService pdfExportService) =>
        {
            var einsatz = einsatzService.CurrentEinsatz;
            var fileNamePart = string.IsNullOrWhiteSpace(einsatz.EinsatzNummer)
                ? $"einsatzbericht-{DateTime.Now:yyyyMMdd-HHmmss}"
                : $"einsatzbericht-{einsatz.EinsatzNummer}";

            var bytes = await pdfExportService.ExportEinsatzToPdfBytesAsync(
                einsatz,
                einsatzService.Teams,
                einsatzService.GlobalNotes);

            return Results.File(bytes, "application/pdf", $"{fileNamePart}.pdf");
        });

        app.MapGet("/downloads/einsatz-karte.pdf", async (
            IEinsatzService einsatzService,
            IPdfExportService pdfExportService,
            ICollarTrackingService collarTrackingService,
            ILoggerFactory loggerFactory,
            string? mapType = null,
            string? teamId = null,
            bool? showSearchAreas = null,
            bool? showPoints = null,
            bool? showGps = null,
            bool? showPhone = null,
            string? gridType = null,
            string? zoomMode = null,
            double? centerLat = null,
            double? centerLng = null,
            int? zoom = null) =>
        {
            var tileType = mapType switch
            {
                "satellite" => Einsatzueberwachung.Domain.Models.Enums.MapTileType.Satellite,
                "satelliteGoogle" => Einsatzueberwachung.Domain.Models.Enums.MapTileType.SatelliteGoogle,
                "hybrid" => Einsatzueberwachung.Domain.Models.Enums.MapTileType.Hybrid,
                "topo" => Einsatzueberwachung.Domain.Models.Enums.MapTileType.Topographic,
                _ => Einsatzueberwachung.Domain.Models.Enums.MapTileType.Streets
            };

            var options = new Einsatzueberwachung.Domain.Models.MapPrintOptions
            {
                TileType = tileType,
                FilterTeamId = teamId,
                ShowSearchAreas = showSearchAreas ?? true,
                ShowPointMarkers = showPoints ?? true,
                ShowGpsTracks = showGps ?? false,
                ShowPhoneTracks = showPhone ?? false,
                GridType = gridType is "utm" or "latlon" ? gridType : "none",
                ZoomMode = zoomMode ?? "all",
                CenterLat = centerLat,
                CenterLng = centerLng,
                ZoomLevel = zoom
            };

            var einsatz = einsatzService.CurrentEinsatz;

            // Collect GPS tracks if requested
            List<Einsatzueberwachung.Domain.Models.TeamTrackSnapshot>? gpsTracks = null;
            if (options.ShowGpsTracks)
            {
                gpsTracks = new List<Einsatzueberwachung.Domain.Models.TeamTrackSnapshot>();
                // Add completed track snapshots
                if (einsatz.TrackSnapshots?.Count > 0)
                    gpsTracks.AddRange(einsatz.TrackSnapshots.Where(t => t.Points.Count >= 2));
                // Add live collar histories
                foreach (var collar in collarTrackingService.Collars)
                {
                    var history = collarTrackingService.GetLocationHistory(collar.Id);
                    if (history.Count >= 2)
                    {
                        var matchingTeam = einsatzService.Teams.FirstOrDefault(t => t.CollarId == collar.Id);
                        gpsTracks.Add(new Einsatzueberwachung.Domain.Models.TeamTrackSnapshot
                        {
                            TeamId = matchingTeam?.TeamId ?? string.Empty,
                            TeamName = collar.CollarName ?? collar.Id,
                            CollarName = collar.CollarName ?? collar.Id,
                            Color = "#FF5722",
                            Points = history.Select(h => new Einsatzueberwachung.Domain.Models.TrackPoint
                            {
                                Latitude = h.Latitude,
                                Longitude = h.Longitude,
                                Timestamp = h.Timestamp
                            }).ToList()
                        });
                    }
                }
            }

            // Collect phone track histories if requested
            Dictionary<string, List<Einsatzueberwachung.Domain.Models.TeamPhoneLocation>>? phoneHistories = null;
            if (options.ShowPhoneTracks)
            {
                phoneHistories = einsatzService.GetAllPhoneTrackHistories()
                    .Where(kvp => kvp.Value.Count >= 2)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
            }

            byte[] bytes;
            try
            {
                bytes = await pdfExportService.ExportEinsatzKarteToPdfBytesAsync(
                    einsatz, einsatzService.Teams, options, gpsTracks, phoneHistories);
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("EinsatzKartePdf")
                    .LogError(ex, "Fehler beim Generieren der Einsatzkarte (TileType={TileType})", tileType);
                return Results.Problem("Karte konnte nicht generiert werden. Bitte erneut versuchen.", statusCode: 500);
            }

            var fileNamePart = string.IsNullOrWhiteSpace(einsatz.EinsatzNummer)
                ? $"einsatz-karte-{DateTime.Now:yyyyMMdd-HHmmss}"
                : $"einsatz-karte-{einsatz.EinsatzNummer}";
            return Results.File(bytes, "application/pdf", $"{fileNamePart}.pdf");
        });

        app.MapGet("/downloads/einsatz-archiv/{id}.pdf", async (string id, IArchivService archivService, IPdfExportService pdfExportService) =>
        {
            var archivedEinsatz = await archivService.GetByIdAsync(id);
            if (archivedEinsatz is null)
            {
                return Results.NotFound();
            }

            var includeTracks = archivedEinsatz.TrackSnapshots?.Any(track => track.Points.Count >= 2) == true;
            var bytes = await pdfExportService.ExportArchivedEinsatzToPdfBytesAsync(archivedEinsatz, includeTracks);
            var fileNamePart = string.IsNullOrWhiteSpace(archivedEinsatz.EinsatzNummer)
                ? $"einsatz-archiv-{archivedEinsatz.EinsatzDatum:yyyyMMdd-HHmmss}"
                : $"einsatz-archiv-{archivedEinsatz.EinsatzNummer}";

            return Results.File(bytes, "application/pdf", $"{fileNamePart}.pdf");
        });

        app.MapGet("/downloads/einsatz-archiv.json", async (IArchivService archivService) =>
        {
            var bytes = await archivService.ExportAllAsJsonAsync();
            return Results.File(bytes, "application/json", $"einsatz-archiv-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        });

        app.MapGet("/downloads/einsatz-bericht.xlsx", async (IEinsatzService einsatzService, IExcelExportService excelExportService) =>
        {
            var einsatz = einsatzService.CurrentEinsatz;
            var teams = einsatzService.Teams;
            var notes = einsatzService.GlobalNotes;
            var fileNamePart = string.IsNullOrWhiteSpace(einsatz.EinsatzNummer)
                ? $"einsatzbericht-{DateTime.Now:yyyyMMdd-HHmmss}"
                : einsatz.EinsatzNummer.Replace("/", "-").Replace(" ", "_");
            var bytes = await excelExportService.ExportEinsatzAsync(einsatz, teams, notes);
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{fileNamePart}.xlsx");
        });

        app.MapGet("/downloads/app-settings.json", async (ISettingsService settingsService) =>
        {
            var settings = await settingsService.GetAppSettingsAsync();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true });
            return Results.File(bytes, "application/json", "app-settings.json");
        });

        app.MapGet("/downloads/staffel-settings.json", async (ISettingsService settingsService) =>
        {
            var settings = await settingsService.GetStaffelSettingsAsync();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true });
            return Results.File(bytes, "application/json", "staffel-settings.json");
        });

        app.MapGet("/downloads/staffel-logo", async (ISettingsService settingsService) =>
        {
            var settings = await settingsService.GetStaffelSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.StaffelLogoPfad))
            {
                return Results.NotFound();
            }

            var logoPath = settings.StaffelLogoPfad;
            if (!Path.IsPathRooted(logoPath))
            {
                logoPath = Path.Combine(AppPathResolver.GetDataDirectory(), logoPath.TrimStart('/', '\\'));
            }

            if (!File.Exists(logoPath))
            {
                return Results.NotFound();
            }

            var contentTypeProvider = new FileExtensionContentTypeProvider();
            if (!contentTypeProvider.TryGetContentType(logoPath, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            return Results.File(await File.ReadAllBytesAsync(logoPath), contentType);
        });

        app.MapGet("/downloads/session-data.json", async (IMasterDataService masterDataService) =>
        {
            var sessionData = await masterDataService.LoadSessionDataAsync();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(sessionData, new JsonSerializerOptions { WriteIndented = true });
            return Results.File(bytes, "application/json", "session-data.json");
        });

        app.MapGet("/downloads/stammdaten.xlsx", async (IExcelExportService excelExportService) =>
        {
            var bytes = await excelExportService.ExportStammdatenAsync();
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "stammdaten.xlsx");
        });

        app.MapGet("/downloads/stammdaten-template.xlsx", async (IExcelExportService excelExportService) =>
        {
            var bytes = await excelExportService.CreateImportTemplateAsync();
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "stammdaten-template.xlsx");
        });

        app.MapGet("/downloads/data-backup.zip", () =>
        {
            var dataDirectory = AppPathResolver.GetDataDirectory();
            if (!Directory.Exists(dataDirectory))
            {
                return Results.NotFound();
            }

            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var files = Directory.GetFiles(dataDirectory, "*", SearchOption.AllDirectories);
                foreach (var filePath in files)
                {
                    var relativePath = Path.GetRelativePath(dataDirectory, filePath);
                    archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                }
            }

            var fileName = $"einsatzueberwachung-data-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            return Results.File(memoryStream.ToArray(), "application/zip", fileName);
        });

        app.MapGet("/downloads/livetracking.zip", () =>
        {
            var candidateZipFiles = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "livetracking.zip"),
                Path.Combine(AppPathResolver.GetDataDirectory(), "livetracking.zip"),
                Path.Combine(AppPathResolver.GetDataDirectory(), "livetracking", "livetracking.zip"),
                Path.Combine(AppContext.BaseDirectory, "wwwroot", "downloads", "livetracking.zip")
            };

            foreach (var zipPath in candidateZipFiles)
            {
                if (File.Exists(zipPath))
                {
                    var fileName = $"einsatzueberwachung-livetracking-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
                    return Results.File(File.ReadAllBytes(zipPath), "application/zip", fileName);
                }
            }

            var candidateDirectories = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "livetracking"),
                Path.Combine(AppPathResolver.GetDataDirectory(), "livetracking"),
                Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "Einsatzueberwachung.LiveTracking", "bin", "Release", "net9.0-windows7.0")),
                Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "Einsatzueberwachung.LiveTracking", "bin", "Debug", "net9.0-windows7.0"))
            };

            var sourceDirectory = candidateDirectories
                .Where(Directory.Exists)
                .FirstOrDefault(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any());

            if (sourceDirectory is null)
            {
                return Results.NotFound("LiveTracking-Paket wurde auf diesem System noch nicht bereitgestellt. Bitte legen Sie 'livetracking.zip' im Datenverzeichnis ab.");
            }

            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
                foreach (var filePath in files)
                {
                    var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
                    archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                }
            }

            var bundledFileName = $"einsatzueberwachung-livetracking-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            return Results.File(memoryStream.ToArray(), "application/zip", bundledFileName);
        });

        return app;
    }
}

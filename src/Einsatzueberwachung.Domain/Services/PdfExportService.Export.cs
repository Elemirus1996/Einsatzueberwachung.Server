using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class PdfExportService
    {
        public Task<PdfExportResult> ExportEinsatzToPdfAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes)
            => ExportEinsatzToPdfAsync(einsatzData, teams, notes, false);

        public async Task<PdfExportResult> ExportEinsatzToPdfAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes, bool includeTracks)
        {
            try
            {
                var staffelInfo = await ResolveStaffelInfoAsync(einsatzData);
                var filename = $"Einsatzbericht_{einsatzData.EinsatzNummer}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                var einsatzPath = AppPathResolver.GetReportDirectory();
                var filePath = Path.Combine(einsatzPath, filename);
                var tracks = includeTracks ? einsatzData.TrackSnapshots : null;
                List<SearchAreaCoverageResult>? coverage = null;
                byte[]? coverageHeatmap = null;

                if (tracks != null && _mapRenderer != null)
                {
                    await RenderTrackMapsAsync(tracks);
                    (coverage, coverageHeatmap) = await CalculateAndRenderCoverageAsync(tracks, einsatzData.ElwPosition);
                }

                var pdfDocument = CreateEinsatzDocument(einsatzData, teams, notes, staffelInfo, tracks, coverage, coverageHeatmap);

                await Task.Run(() =>
                {
                    pdfDocument.GeneratePdf(filePath);
                });

                return new PdfExportResult
                {
                    Success = true,
                    FilePath = filePath
                };
            }
            catch (Exception ex)
            {
                return new PdfExportResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public Task<PdfExportResult> ExportArchivedEinsatzToPdfAsync(ArchivedEinsatz archivedEinsatz)
            => ExportArchivedEinsatzToPdfAsync(archivedEinsatz, false);

        public async Task<PdfExportResult> ExportArchivedEinsatzToPdfAsync(ArchivedEinsatz archivedEinsatz, bool includeTracks)
        {
            try
            {
                var staffelInfo = await ResolveStaffelInfoAsync(archivedEinsatz);
                var filename = $"Einsatzbericht_{archivedEinsatz.EinsatzNummer}_{archivedEinsatz.EinsatzDatum:yyyyMMdd}.pdf";
                var einsatzPath = AppPathResolver.GetReportDirectory();
                var filePath = Path.Combine(einsatzPath, filename);
                var tracks = includeTracks ? archivedEinsatz.TrackSnapshots : null;
                List<SearchAreaCoverageResult>? coverage = null;
                byte[]? coverageHeatmap = null;

                if (tracks?.Any(t => t.Points.Count >= 2) == true && _mapRenderer != null)
                {
                    await RenderTrackMapsAsync(tracks);
                    (coverage, coverageHeatmap) = await CalculateAndRenderCoverageAsync(tracks, archivedEinsatz.ElwPosition);
                }

                var pdfDocument = CreateArchivedEinsatzDocument(archivedEinsatz, staffelInfo, tracks, coverage, coverageHeatmap);

                await Task.Run(() =>
                {
                    pdfDocument.GeneratePdf(filePath);
                });

                return new PdfExportResult
                {
                    Success = true,
                    FilePath = filePath
                };
            }
            catch (Exception ex)
            {
                return new PdfExportResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<byte[]> ExportArchivedEinsatzToPdfBytesAsync(ArchivedEinsatz archivedEinsatz)
            => await ExportArchivedEinsatzToPdfBytesAsync(archivedEinsatz, false);

        public async Task<byte[]> ExportArchivedEinsatzToPdfBytesAsync(ArchivedEinsatz archivedEinsatz, bool includeTracks)
        {
            var staffelInfo = await ResolveStaffelInfoAsync(archivedEinsatz);
            var tracks = includeTracks ? archivedEinsatz.TrackSnapshots : null;
            List<SearchAreaCoverageResult>? coverage = null;
            byte[]? coverageHeatmap = null;

            if (tracks?.Any(t => t.Points.Count >= 2) == true && _mapRenderer != null)
            {
                await RenderTrackMapsAsync(tracks);
                (coverage, coverageHeatmap) = await CalculateAndRenderCoverageAsync(tracks, archivedEinsatz.ElwPosition);
            }

            var pdfDocument = CreateArchivedEinsatzDocument(archivedEinsatz, staffelInfo, tracks, coverage, coverageHeatmap);

            return await Task.Run(() =>
            {
                using var stream = new MemoryStream();
                pdfDocument.GeneratePdf(stream);
                return stream.ToArray();
            });
        }

        public Task<byte[]> ExportEinsatzToPdfBytesAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes)
            => ExportEinsatzToPdfBytesAsync(einsatzData, teams, notes, false);

        public async Task<byte[]> ExportEinsatzToPdfBytesAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes, bool includeTracks)
        {
            var staffelInfo = await ResolveStaffelInfoAsync(einsatzData);
            var tracks = includeTracks ? einsatzData.TrackSnapshots : null;
            List<SearchAreaCoverageResult>? coverage = null;
            byte[]? coverageHeatmap = null;

            if (tracks?.Any(t => t.Points.Count >= 2) == true && _mapRenderer != null)
            {
                await RenderTrackMapsAsync(tracks);
                (coverage, coverageHeatmap) = await CalculateAndRenderCoverageAsync(tracks, einsatzData.ElwPosition);
            }

            var pdfDocument = CreateEinsatzDocument(einsatzData, teams, notes, staffelInfo, tracks, coverage, coverageHeatmap);
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream();
                pdfDocument.GeneratePdf(stream);
                return stream.ToArray();
            });
        }

        private async Task RenderTrackMapsAsync(List<TeamTrackSnapshot> tracks)
        {
            foreach (var track in tracks.Where(t => t.Points.Count >= 2))
            {
                try
                {
                    var imageBytes = await _mapRenderer!.RenderTrackMapAsync(
                        track.Points,
                        track.SearchAreaCoordinates?.Count >= 3 ? track.SearchAreaCoordinates : null,
                        "#000000",
                        track.SearchAreaColor,
                        800, 450);

                    if (imageBytes != null)
                    {
                        track.MapImageBase64 = Convert.ToBase64String(imageBytes);
                    }
                }
                catch
                {
                    // Fallback: SVG wird in ComposeGpsTracks verwendet
                }
            }
        }

        private async Task<(List<SearchAreaCoverageResult>? Coverage, byte[]? Heatmap)> CalculateAndRenderCoverageAsync(
            List<TeamTrackSnapshot> tracks, (double Latitude, double Longitude)? elwPosition)
        {
            var coverage = CoverageAnalysisCalculator.Calculate(tracks);
            if (coverage.Count == 0)
                return (null, null);

            byte[]? heatmap = null;
            try
            {
                heatmap = await _mapRenderer!.RenderCoverageHeatmapAsync(coverage, elwPosition);
            }
            catch
            {
                // Kein Heatmap-Bild verfügbar, Tabelle wird trotzdem angezeigt
            }

            return (coverage, heatmap);
        }

        private Document CreateEinsatzDocument(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes, StaffelInfo staffelInfo, List<TeamTrackSnapshot>? tracks = null, List<SearchAreaCoverageResult>? coverage = null, byte[]? coverageHeatmap = null)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header()
                        .BorderTop(4).BorderColor(einsatzData.IstEinsatz ? "#C0392B" : "#2C3E50")
                        .Background(Colors.White)
                        .Padding(20)
                        .Element(c => ComposeReportHeader(c, einsatzData.IstEinsatz ? "EINSATZBERICHT" : "ÜBUNGSBERICHT",
                            einsatzData.IstEinsatz ? "#C0392B" : "#2C3E50", staffelInfo));

                    page.Content()
                        .Column(column =>
                        {
                            column.Item().PaddingVertical(10).Element(c => ComposeGrunddaten(c, einsatzData));
                            column.Item().PaddingVertical(10).Element(c => ComposeZusammenfassung(c, teams.Count,
                                einsatzData.SearchAreas.Count, notes.Count,
                                FormatTimeSpan(teams.Aggregate(TimeSpan.Zero, (a, t) => a + t.ElapsedTime)),
                                null));
                            if (einsatzData.Vermisste?.Any() == true)
                                column.Item().PaddingVertical(10).Element(c => ComposeVermisste(c, einsatzData.Vermisste));
                            column.Item().PaddingVertical(10).Element(c => ComposeTeams(c, teams, notes));
                            if (einsatzData.SearchAreas.Any())
                                column.Item().PaddingVertical(10).Element(c => ComposeSuchgebiete(c, einsatzData.SearchAreas.ToList()));
                            if (tracks?.Any() == true)
                            {
                                column.Item().PageBreak();
                                column.Item().PaddingVertical(10).Element(c => ComposeGpsTracks(c, tracks));
                            }
                            if (coverage?.Any() == true)
                            {
                                column.Item().PageBreak();
                                column.Item().PaddingVertical(10).Element(c => ComposeCoverageAnalysis(c, coverage, coverageHeatmap));
                            }
                            if (notes.Any())
                            {
                                column.Item().PageBreak();
                                column.Item().PaddingVertical(10).Element(c => ComposeNotizen(c, notes));
                            }
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.Span("Erstellt am: ");
                            text.Span($"{Now:dd.MM.yyyy HH:mm:ss}").Bold();
                            text.Span(" | Seite ");
                            text.CurrentPageNumber();
                            text.Span(" von ");
                            text.TotalPages();
                        });
                });
            });
        }

        private Document CreateArchivedEinsatzDocument(ArchivedEinsatz einsatz, StaffelInfo staffelInfo, List<TeamTrackSnapshot>? tracks = null, List<SearchAreaCoverageResult>? coverage = null, byte[]? coverageHeatmap = null)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header()
                        .BorderTop(4).BorderColor(einsatz.IstEinsatz ? "#C0392B" : "#2C3E50")
                        .Background(Colors.White)
                        .Padding(20)
                        .Element(c => ComposeReportHeader(c,
                            einsatz.IstEinsatz ? "EINSATZBERICHT" : "ÜBUNGSBERICHT",
                            einsatz.IstEinsatz ? "#C0392B" : "#2C3E50",
                            staffelInfo));

                    page.Content()
                        .Column(column =>
                        {
                            column.Item().PaddingVertical(10).Element(c => ComposeArchivedGrunddaten(c, einsatz));
                            column.Item().PaddingVertical(10).Element(c => ComposeZusammenfassung(c,
                                einsatz.AnzahlTeams, einsatz.SearchAreas?.Count ?? 0,
                                einsatz.GlobalNotesEntries?.Count ?? 0,
                                einsatz.DauerFormatiert,
                                einsatz.Ergebnis));
                            column.Item().PaddingVertical(10).Element(c => ComposeErgebnis(c, einsatz));
                            if ((einsatz.PersonalNamen?.Any() ?? false) || (einsatz.HundeNamen?.Any() ?? false))
                                column.Item().PaddingVertical(10).Element(c => ComposeVorOrtChecklisten(c, einsatz));
                            if (einsatz.Vermisste?.Any() == true)
                                column.Item().PaddingVertical(10).Element(c => ComposeVermisste(c, einsatz.Vermisste));
                            if (einsatz.Teams?.Any() == true)
                                column.Item().PaddingVertical(10).Element(c => ComposeArchivedTeams(c, einsatz.Teams, einsatz.GlobalNotesEntries ?? new()));
                            if (einsatz.SearchAreas?.Any() == true)
                                column.Item().PaddingVertical(10).Element(c => ComposeSuchgebiete(c, einsatz.SearchAreas));
                            if (tracks?.Any() == true)
                            {
                                column.Item().PageBreak();
                                column.Item().PaddingVertical(10).Element(c => ComposeGpsTracks(c, tracks));
                            }
                            if (coverage?.Any() == true)
                            {
                                column.Item().PageBreak();
                                column.Item().PaddingVertical(10).Element(c => ComposeCoverageAnalysis(c, coverage, coverageHeatmap));
                            }
                            if (einsatz.GlobalNotesEntries?.Any() == true)
                            {
                                column.Item().PageBreak();
                                column.Item().PaddingVertical(10).Element(c => ComposeNotizen(c, einsatz.GlobalNotesEntries));
                            }
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(text =>
                        {
                            text.Span("Archiviert am: ");
                            text.Span($"{einsatz.ArchivedAt:dd.MM.yyyy HH:mm}").Bold();
                            text.Span(" | Erstellt am: ");
                            text.Span($"{Now:dd.MM.yyyy HH:mm}");
                            text.Span(" | Seite ");
                            text.CurrentPageNumber();
                            text.Span(" von ");
                            text.TotalPages();
                        });
                });
            });
        }
    }
}

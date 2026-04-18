// PDF-Export-Service mit QuestPDF
// Erstellt professionelle PDF-Berichte für Einsätze

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Einsatzueberwachung.Domain.Services
{
    public class PdfExportService : IPdfExportService
    {
        private readonly ISettingsService? _settingsService;
        private readonly ITimeService? _timeService;
        private readonly IStaticMapRenderer? _mapRenderer;

        public PdfExportService(ISettingsService? settingsService = null, ITimeService? timeService = null, IStaticMapRenderer? mapRenderer = null)
        {
            _settingsService = settingsService;
            _timeService = timeService;
            _mapRenderer = mapRenderer;
            // QuestPDF Lizenz-Konfiguration (Community License für nicht-kommerzielle Nutzung)
            QuestPDF.Settings.License = LicenseType.Community;
        }

        private DateTime Now => _timeService?.Now ?? DateTime.Now;

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

                // Kartenbilder für Tracks rendern (Server-seitig OSM-Tiles)
                if (tracks != null && _mapRenderer != null)
                {
                    await RenderTrackMapsAsync(tracks);
                }

                var pdfDocument = CreateEinsatzDocument(einsatzData, teams, notes, staffelInfo, tracks);

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

        private async Task RenderTrackMapsAsync(List<TeamTrackSnapshot> tracks)
        {
            foreach (var track in tracks.Where(t => t.Points.Count >= 2))
            {
                try
                {
                    var imageBytes = await _mapRenderer!.RenderTrackMapAsync(
                        track.Points,
                        track.SearchAreaCoordinates?.Count >= 3 ? track.SearchAreaCoordinates : null,
                        track.Color,
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

        private void ComposeGrunddaten(IContainer container, EinsatzData einsatzData)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, "Grunddaten"));

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(150);
                        columns.RelativeColumn();
                    });

                    AddTableRow(table, "Einsatznummer:", einsatzData.EinsatzNummer);
                    AddTableRow(table, "Einsatztyp:", einsatzData.EinsatzTyp);
                    AddTableRow(table, "Datum:", einsatzData.EinsatzDatum.ToString("dd.MM.yyyy HH:mm"));
                    
                    if (einsatzData.AlarmierungsZeit.HasValue)
                    {
                        AddTableRow(table, "Alarmierung:", einsatzData.AlarmierungsZeit.Value.ToString("dd.MM.yyyy HH:mm"));
                    }
                    
                    AddTableRow(table, "Einsatzort:", einsatzData.Einsatzort);
                    AddTableRow(table, "Alarmiert durch:", einsatzData.Alarmiert);
                    AddTableRow(table, "Einsatzleiter:", einsatzData.Einsatzleiter);
                    AddTableRow(table, "Führungsassistent:", einsatzData.Fuehrungsassistent);
                });
            });
        }

        private void ComposeTeams(IContainer container, List<Team> teams, List<GlobalNotesEntry> notes)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, $"Teams ({teams.Count})"));

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);      // Team
                        columns.RelativeColumn(2);      // Hund/Drohne
                        columns.RelativeColumn(2);      // Personal
                        columns.RelativeColumn(1.5f);   // Suchgebiet
                        columns.ConstantColumn(55);     // Ausrücken
                        columns.ConstantColumn(60);     // Einsatzzeit
                    });

                    // Header
                    table.Header(header =>
                    {
                        header.Cell().Element(CellStyleHeader).Text("Team").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Hund / Drohne").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Personal").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Suchgebiet").Bold();
                        header.Cell().Element(CellStyleHeader).AlignCenter().Text("Ausrücken").Bold();
                        header.Cell().Element(CellStyleHeader).AlignCenter().Text("Einsatzzeit").Bold();

                        static IContainer CellStyleHeader(IContainer c) =>
                            c.Background(Colors.Blue.Darken1).Padding(5);
                    });

                    // Rows
                    var rowIndex = 0;
                    foreach (var team in teams)
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Element(c => CellStyle(c, bg)).Text(team.TeamName);

                        table.Cell().Element(c => CellStyle(c, bg)).Column(col =>
                        {
                            if (team.IsDroneTeam)
                                col.Item().Text($"Drohne: {team.DroneType}").FontSize(9);
                            else if (!string.IsNullOrEmpty(team.DogName))
                            {
                                col.Item().Text(team.DogName).FontSize(9);
                                col.Item().Text($"({team.DogSpecialization.GetShortName()})").FontSize(8).Italic();
                            }
                            else
                                col.Item().Text("Support").FontSize(9).Italic();
                        });

                        table.Cell().Element(c => CellStyle(c, bg)).Column(col =>
                        {
                            col.Item().Text(team.HundefuehrerName).FontSize(9);
                            if (!string.IsNullOrEmpty(team.HelferName))
                                col.Item().Text($"Helfer: {team.HelferName}").FontSize(8).Italic();
                        });

                        table.Cell().Element(c => CellStyle(c, bg)).Text(team.SearchAreaName ?? "-").FontSize(9);

                        // Ausrücken – erster TeamStart-Zeitstempel aus den Notizen
                        var startNote = notes
                            .Where(n => n.SourceTeamId == team.TeamId && n.Type == GlobalNotesEntryType.TeamStart)
                            .OrderBy(n => n.Timestamp)
                            .FirstOrDefault();
                        var ausrueckText = startNote != null ? startNote.Timestamp.ToString("HH:mm") : "-";
                        table.Cell().Element(c => CellStyle(c, bg)).AlignCenter().Text(ausrueckText).FontSize(9);

                        table.Cell().Element(c => CellStyle(c, bg)).AlignCenter().Column(col =>
                        {
                            col.Item().Text(FormatTimeSpan(team.ElapsedTime)).FontSize(9);
                            if (team.IsSecondWarning)
                                col.Item().Text("KRITISCH").FontSize(7).Bold().FontColor(Colors.Red.Medium);
                            else if (team.IsFirstWarning)
                                col.Item().Text("Warnung").FontSize(7).FontColor(Colors.Orange.Medium);
                        });

                        static IContainer CellStyle(IContainer c, string bg) =>
                            c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
                    }
                });
            });
        }

        private void ComposeSuchgebiete(IContainer container, List<SearchArea> searchAreas)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, $"Suchgebiete ({searchAreas.Count})"));

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(3);
                    });

                    // Header
                    table.Header(header =>
                    {
                        header.Cell().Element(CellStyleHeader).Text("Name").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Team").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Status").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Kartenlayout").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Notizen").Bold();

                        static IContainer CellStyleHeader(IContainer c) =>
                            c.Background(Colors.Blue.Darken1).Padding(5);
                    });

                    var rowIndex = 0;
                    foreach (var area in searchAreas)
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Element(c => CellStyle(c, bg)).Text(area.Name);
                        table.Cell().Element(c => CellStyle(c, bg)).Text(area.AssignedTeamName ?? "-");
                        table.Cell().Element(c => CellStyle(c, bg))
                            .Text(area.IsCompleted ? "Abgeschlossen" : "In Bearbeitung")
                            .FontColor(area.IsCompleted ? Colors.Green.Medium : Colors.Orange.Medium);
                        table.Cell().Element(c => CellStyle(c, bg)).Column(col =>
                        {
                            col.Item().Text($"Fläche: {area.FormattedArea}").FontSize(9);
                            col.Item().Text($"Punkte: {area.Coordinates?.Count ?? 0}").FontSize(8).Italic();
                        });
                        table.Cell().Element(c => CellStyle(c, bg)).Text(area.Notes ?? "-").FontSize(9);

                        static IContainer CellStyle(IContainer c, string bg) =>
                            c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
                    }
                });
            });
        }

        private void ComposeNotizen(IContainer container, List<GlobalNotesEntry> notes)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, $"Funksprüche & Notizen ({notes.Count})"));

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(96);    // Zeit
                        columns.ConstantColumn(80);    // Typ
                        columns.ConstantColumn(90);    // Team/Quelle
                        columns.RelativeColumn();      // Text
                    });

                    // Header
                    table.Header(header =>
                    {
                        header.Cell().Element(HdrStyle).Text("Zeit").Bold();
                        header.Cell().Element(HdrStyle).Text("Typ").Bold();
                        header.Cell().Element(HdrStyle).Text("Quelle").Bold();
                        header.Cell().Element(HdrStyle).Text("Text").Bold();

                        static IContainer HdrStyle(IContainer c) =>
                            c.Background(Colors.Blue.Darken1).Padding(5);
                    });

                    var rowIndex = 0;
                    foreach (var note in notes.OrderBy(n => n.Timestamp))
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                        var typeColor = GetNoteTypeColor(note.Type);

                        // Zeit
                        table.Cell().Element(c => RowCell(c, bg))
                            .AlignCenter().Text($"{note.Timestamp:dd.MM.\nHH:mm}").FontSize(8);

                        // Typ als farbige Pille
                        table.Cell().Element(c => c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4))
                            .Background(typeColor).Padding(3).AlignCenter()
                            .Text(GetNoteTypeLabel(note.Type)).FontSize(8).Bold().FontColor(Colors.White);

                        // Quelle/Team
                        table.Cell().Element(c => RowCell(c, bg))
                            .AlignCenter().Text(note.SourceTeamName ?? "-").FontSize(8).Italic();

                        // Text
                        table.Cell().Element(c => RowCell(c, bg))
                            .Text(note.Text).FontSize(9);

                        static IContainer RowCell(IContainer c, string bg) =>
                            c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
                    }
                });
            });
        }

        private void AddTableRow(TableDescriptor table, string label, string value)
        {
            table.Cell().Text(label).Bold();
            table.Cell().Text(value ?? "-");
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private string GetNoteTypeColor(GlobalNotesEntryType type)
        {
            return type switch
            {
                GlobalNotesEntryType.TeamStart => Colors.Green.Medium,
                GlobalNotesEntryType.TeamStop => Colors.Orange.Medium,
                GlobalNotesEntryType.TeamWarning => Colors.Red.Medium,
                GlobalNotesEntryType.TeamReset => Colors.Blue.Medium,
                GlobalNotesEntryType.EinsatzUpdate => Colors.Blue.Darken1,
                GlobalNotesEntryType.System => Colors.Grey.Darken1,
                _ => Colors.Grey.Darken1
            };
        }

        private static string GetNoteTypeLabel(GlobalNotesEntryType type)
        {
            return type switch
            {
                GlobalNotesEntryType.TeamStart => "Ausrücken",
                GlobalNotesEntryType.TeamStop => "Einrücken",
                GlobalNotesEntryType.TeamWarning => "Warnung",
                GlobalNotesEntryType.TeamReset => "Reset",
                GlobalNotesEntryType.EinsatzUpdate => "Einsatz",
                GlobalNotesEntryType.System => "System",
                GlobalNotesEntryType.Manual => "Notiz",
                _ => type.ToString()
            };
        }

        /// <summary>
        /// Exportiert einen archivierten Einsatz als PDF (speichert Datei)
        /// </summary>
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
                byte[]? archivedTrackMapImage = null;

                if (tracks?.Any(track => track.Points.Count >= 2) == true && _mapRenderer != null)
                {
                    archivedTrackMapImage = await _mapRenderer.RenderCombinedTrackMapAsync(tracks, archivedEinsatz.ElwPosition);
                }

                var pdfDocument = CreateArchivedEinsatzDocument(archivedEinsatz, staffelInfo, tracks, archivedTrackMapImage);

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

        /// <summary>
        /// Exportiert einen archivierten Einsatz als PDF-Byte-Array (für Browser-Download)
        /// </summary>
        public async Task<byte[]> ExportArchivedEinsatzToPdfBytesAsync(ArchivedEinsatz archivedEinsatz)
            => await ExportArchivedEinsatzToPdfBytesAsync(archivedEinsatz, false);

        /// <summary>
        /// Exportiert einen archivierten Einsatz als PDF-Byte-Array (für Browser-Download)
        /// </summary>
        public async Task<byte[]> ExportArchivedEinsatzToPdfBytesAsync(ArchivedEinsatz archivedEinsatz, bool includeTracks)
        {
            var staffelInfo = await ResolveStaffelInfoAsync(archivedEinsatz);
            var tracks = includeTracks ? archivedEinsatz.TrackSnapshots : null;
            byte[]? archivedTrackMapImage = null;

            if (tracks?.Any(track => track.Points.Count >= 2) == true && _mapRenderer != null)
            {
                archivedTrackMapImage = await _mapRenderer.RenderCombinedTrackMapAsync(tracks, archivedEinsatz.ElwPosition);
            }

            var pdfDocument = CreateArchivedEinsatzDocument(archivedEinsatz, staffelInfo, tracks, archivedTrackMapImage);
            
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream();
                pdfDocument.GeneratePdf(stream);
                return stream.ToArray();
            });
        }

        /// <summary>
        /// Exportiert einen aktiven Einsatz als PDF-Byte-Array (für Browser-Download)
        /// </summary>
        public Task<byte[]> ExportEinsatzToPdfBytesAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes)
            => ExportEinsatzToPdfBytesAsync(einsatzData, teams, notes, false);

        public async Task<byte[]> ExportEinsatzToPdfBytesAsync(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes, bool includeTracks)
        {
            var staffelInfo = await ResolveStaffelInfoAsync(einsatzData);
            var tracks = includeTracks ? einsatzData.TrackSnapshots : null;
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream();
                var pdfDocument = CreateEinsatzDocument(einsatzData, teams, notes, staffelInfo, tracks);
                pdfDocument.GeneratePdf(stream);
                
                return stream.ToArray();
            });
        }

        private Document CreateEinsatzDocument(EinsatzData einsatzData, List<Team> teams, List<GlobalNotesEntry> notes, StaffelInfo staffelInfo, List<TeamTrackSnapshot>? tracks = null)
        {
            return Document.Create(container =>
            {
                // ─── Hauptbericht ────────────────────────────────────────────
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header()
                        .Background(einsatzData.IstEinsatz ? Colors.Red.Lighten3 : Colors.Blue.Lighten3)
                        .Padding(20)
                        .Element(c => ComposeReportHeader(c, einsatzData.IstEinsatz ? "EINSATZBERICHT" : "ÜBUNGSBERICHT",
                            einsatzData.IstEinsatz ? Colors.Red.Darken2 : Colors.Blue.Darken2, staffelInfo));

                    page.Content()
                        .Column(column =>
                        {
                            column.Item().PaddingVertical(10).Element(c => ComposeGrunddaten(c, einsatzData));
                            column.Item().PaddingVertical(10).Element(c => ComposeZusammenfassung(c, teams.Count,
                                einsatzData.SearchAreas.Count, notes.Count,
                                FormatTimeSpan(teams.Aggregate(TimeSpan.Zero, (a, t) => a + t.ElapsedTime)),
                                null));
                            column.Item().PaddingVertical(10).Element(c => ComposeTeams(c, teams, notes));
                            if (einsatzData.SearchAreas.Any())
                                column.Item().PaddingVertical(10).Element(c => ComposeSuchgebiete(c, einsatzData.SearchAreas.ToList()));
                            if (tracks?.Any() == true)
                            {
                                column.Item().PageBreak();
                                column.Item().PaddingVertical(10).Element(c => ComposeGpsTracks(c, tracks));
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

        /// <summary>
        /// Erstellt das PDF-Dokument für einen archivierten Einsatz
        /// </summary>
        private Document CreateArchivedEinsatzDocument(ArchivedEinsatz einsatz, StaffelInfo staffelInfo, List<TeamTrackSnapshot>? tracks = null, byte[]? archivedTrackMapImage = null)
        {
            return Document.Create(container =>
            {
                // ─── Hauptbericht ────────────────────────────────────────────
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header()
                        .Background(einsatz.IstEinsatz ? Colors.Red.Lighten3 : Colors.Blue.Lighten3)
                        .Padding(20)
                        .Element(c => ComposeReportHeader(c,
                            einsatz.IstEinsatz ? "EINSATZBERICHT" : "ÜBUNGSBERICHT",
                            einsatz.IstEinsatz ? Colors.Red.Darken2 : Colors.Blue.Darken2,
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
                            if (einsatz.Teams?.Any() == true)
                                column.Item().PaddingVertical(10).Element(c => ComposeArchivedTeams(c, einsatz.Teams, einsatz.GlobalNotesEntries ?? new()));
                            if (einsatz.SearchAreas?.Any() == true)
                                column.Item().PaddingVertical(10).Element(c => ComposeSuchgebiete(c, einsatz.SearchAreas));
                            if (tracks?.Any() == true)
                            {
                                column.Item().PageBreak();
                                column.Item().PaddingVertical(10).Element(c => ComposeArchivedGpsTrackMap(c, einsatz, tracks, archivedTrackMapImage));
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

        // ─────────────────────────────────────────────────────────────────────────
        // Zusammenfassungs-Box
        // ─────────────────────────────────────────────────────────────────────────

        private static void ComposeZusammenfassung(IContainer container,
            int anzahlTeams, int anzahlSuchgebiete, int anzahlNotizen,
            string gesamtdauer, string? ergebnis)
        {
            container.Background(Colors.Grey.Lighten4).Padding(12).Column(col =>
            {
                col.Item().Text("Zusammenfassung").FontSize(11).Bold().FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(8).Row(row =>
                {
                    StatBox(row.RelativeItem(), $"{anzahlTeams}", "Teams", Colors.Blue.Lighten3);
                    row.ConstantItem(6);
                    StatBox(row.RelativeItem(), $"{anzahlSuchgebiete}", "Suchgebiete", Colors.Teal.Lighten3);
                    row.ConstantItem(6);
                    StatBox(row.RelativeItem(), $"{anzahlNotizen}", "Notizen / Funk", Colors.Purple.Lighten3);
                    row.ConstantItem(6);
                    StatBox(row.RelativeItem(),
                        !string.IsNullOrWhiteSpace(ergebnis) ? ergebnis! : gesamtdauer,
                        !string.IsNullOrWhiteSpace(ergebnis) ? "Ergebnis" : "Gesamtdauer",
                        Colors.Orange.Lighten3);
                });
            });

            static void StatBox(IContainer c, string value, string label, string bg)
            {
                c.Background(bg).Border(1).BorderColor(Colors.White).Padding(10).Column(inner =>
                {
                    inner.Item().AlignCenter().Text(value).FontSize(18).Bold();
                    inner.Item().AlignCenter().Text(label).FontSize(9).FontColor(Colors.Grey.Darken2);
                });
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Farbcodierter Abschnittsheader
        // ─────────────────────────────────────────────────────────────────────────

        private static void ComposeSectionHeader(IContainer container, string title, string? iconText = null)
        {
            container.Background(Colors.Blue.Darken1).Padding(8).Row(row =>
            {
                if (!string.IsNullOrWhiteSpace(iconText))
                    row.AutoItem().PaddingRight(8).Text(iconText).FontSize(13).FontColor(Colors.White).Bold();
                row.RelativeItem().Text(title).FontSize(13).Bold().FontColor(Colors.White);
            });
        }

        private void ComposeReportHeader(IContainer container, string title, string titleColor, StaffelInfo staffelInfo)        {
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Spacing(2);

                    column.Item().Text(title)
                        .FontSize(24)
                        .Bold()
                        .FontColor(titleColor);

                    if (!string.IsNullOrWhiteSpace(staffelInfo.Name))
                    {
                        column.Item().Text(staffelInfo.Name)
                            .FontSize(14)
                            .FontColor(Colors.Grey.Darken1);
                    }

                    column.Item().Text("Rettungshundestaffel")
                        .FontSize(12)
                        .FontColor(Colors.Grey.Darken1);

                    if (!string.IsNullOrWhiteSpace(staffelInfo.Address))
                    {
                        column.Item().PaddingTop(3).Text($"Adresse: {staffelInfo.Address}")
                            .FontSize(9)
                            .FontColor(Colors.Grey.Darken2);
                    }

                    var kontakt = BuildKontaktLine(staffelInfo);
                    if (!string.IsNullOrWhiteSpace(kontakt))
                    {
                        column.Item().Text(kontakt)
                            .FontSize(9)
                            .FontColor(Colors.Grey.Darken2);
                    }
                });

                row.ConstantItem(122).AlignRight().Element(logoContainer =>
                {
                    var imageContainer = logoContainer
                        .Height(80)
                        .AlignTop()
                        .AlignRight()
                        .Border(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .Background(Colors.White)
                        .Padding(6);

                    if (TryLoadLogoBytes(staffelInfo.LogoPath, out var logoBytes))
                    {
                        imageContainer.Image(logoBytes).FitArea();
                    }
                });
            });
        }

        private static string BuildKontaktLine(StaffelInfo info)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(info.Telefon))
            {
                parts.Add($"Tel: {info.Telefon}");
            }

            if (!string.IsNullOrWhiteSpace(info.Email))
            {
                parts.Add($"E-Mail: {info.Email}");
            }

            return string.Join(" | ", parts);
        }

        private bool TryLoadLogoBytes(string? logoPath, out byte[] logoBytes)
        {
            logoBytes = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(logoPath))
            {
                return false;
            }

            try
            {
                var filePath = ResolveLogoPath(logoPath);
                if (!File.Exists(filePath))
                {
                    return false;
                }

                var extension = Path.GetExtension(filePath);
                if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                logoBytes = File.ReadAllBytes(filePath);
                return logoBytes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveLogoPath(string logoPath)
        {
            if (Path.IsPathRooted(logoPath))
            {
                return logoPath;
            }

            var cleanPath = logoPath.TrimStart('/', '\\');
            return Path.Combine(AppPathResolver.GetDataDirectory(), cleanPath);
        }

        private async Task<StaffelInfo> ResolveStaffelInfoAsync(EinsatzData einsatzData)
        {
            var settings = await GetStaffelSettingsOrDefaultAsync();
            return new StaffelInfo
            {
                Name = PickValue(einsatzData.StaffelName, settings.StaffelName),
                Address = PickValue(einsatzData.StaffelAdresse, settings.StaffelAdresse),
                Telefon = PickValue(einsatzData.StaffelTelefon, settings.StaffelTelefon),
                Email = PickValue(einsatzData.StaffelEmail, settings.StaffelEmail),
                LogoPath = PickValue(einsatzData.StaffelLogoPfad, settings.StaffelLogoPfad)
            };
        }

        private async Task<StaffelInfo> ResolveStaffelInfoAsync(ArchivedEinsatz einsatz)
        {
            var settings = await GetStaffelSettingsOrDefaultAsync();
            return new StaffelInfo
            {
                Name = PickValue(einsatz.StaffelName, settings.StaffelName),
                Address = PickValue(einsatz.StaffelAdresse, settings.StaffelAdresse),
                Telefon = PickValue(einsatz.StaffelTelefon, settings.StaffelTelefon),
                Email = PickValue(einsatz.StaffelEmail, settings.StaffelEmail),
                LogoPath = PickValue(einsatz.StaffelLogoPfad, settings.StaffelLogoPfad)
            };
        }

        private async Task<StaffelSettings> GetStaffelSettingsOrDefaultAsync()
        {
            if (_settingsService is null)
            {
                return new StaffelSettings();
            }

            try
            {
                return await _settingsService.GetStaffelSettingsAsync();
            }
            catch
            {
                return new StaffelSettings();
            }
        }

        private static string PickValue(string preferred, string fallback)
        {
            return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
        }

        private sealed class StaffelInfo
        {
            public string Name { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public string Telefon { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string LogoPath { get; set; } = string.Empty;
        }

        /// <summary>
        /// Komponiert die archivierten Teams
        /// </summary>
        private void ComposeArchivedTeams(IContainer container, List<ArchivedTeam> teams, List<GlobalNotesEntry> notes)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, $"Teams ({teams.Count})"));

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);    // Team
                        columns.RelativeColumn(2);    // Hund/Drohne
                        columns.RelativeColumn(2.5f); // Personal
                        columns.ConstantColumn(55);   // Ausrücken
                        columns.ConstantColumn(55);   // Einrücken
                        columns.ConstantColumn(60);   // Dauer
                        columns.RelativeColumn(1.5f); // Status
                    });

                    // Header
                    table.Header(header =>
                    {
                        header.Cell().Element(HdrStyle).Text("Team").Bold();
                        header.Cell().Element(HdrStyle).Text("Hund/Drohne").Bold();
                        header.Cell().Element(HdrStyle).Text("Personal").Bold();
                        header.Cell().Element(HdrStyle).AlignCenter().Text("Ausrücken").Bold();
                        header.Cell().Element(HdrStyle).AlignCenter().Text("Einrücken").Bold();
                        header.Cell().Element(HdrStyle).AlignCenter().Text("Dauer").Bold();
                        header.Cell().Element(HdrStyle).Text("Status").Bold();

                        static IContainer HdrStyle(IContainer c) =>
                            c.Background(Colors.Blue.Darken1).Padding(5);
                    });

                    var rowIndex = 0;
                    foreach (var team in teams)
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Element(c => RowCell(c, bg)).Text(team.TeamName);

                        table.Cell().Element(c => RowCell(c, bg)).Column(col =>
                        {
                            if (!string.IsNullOrEmpty(team.DroneName))
                                col.Item().Text($"Drohne: {team.DroneName}").FontSize(9);
                            else if (!string.IsNullOrEmpty(team.DogName))
                                col.Item().Text(team.DogName).FontSize(9);
                            else
                                col.Item().Text("-").FontSize(9);
                        });

                        table.Cell().Element(c => RowCell(c, bg)).Column(col =>
                        {
                            if (team.MemberNames.Any())
                                foreach (var member in team.MemberNames)
                                    col.Item().Text(member).FontSize(9);
                            else
                                col.Item().Text("-").FontSize(9);
                        });

                        var startNote = notes
                            .Where(n => n.SourceTeamName == team.TeamName && n.Type == GlobalNotesEntryType.TeamStart)
                            .OrderBy(n => n.Timestamp).FirstOrDefault();
                        var stopNote = notes
                            .Where(n => n.SourceTeamName == team.TeamName && n.Type == GlobalNotesEntryType.TeamStop)
                            .OrderByDescending(n => n.Timestamp).FirstOrDefault();
                        var ausrueckZeit = startNote?.Timestamp ?? team.AusrueckZeit;
                        var einrueckZeit = stopNote?.Timestamp ?? team.EinrueckZeit;

                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter()
                            .Text(ausrueckZeit.HasValue ? ausrueckZeit.Value.ToString("HH:mm") : "-").FontSize(9);

                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter()
                            .Text(einrueckZeit.HasValue ? einrueckZeit.Value.ToString("HH:mm") : "-").FontSize(9);

                        // Einsatzdauer berechnen wenn beide Zeiten vorhanden
                        var dauer = "-";
                        if (ausrueckZeit.HasValue && einrueckZeit.HasValue)
                        {
                            var diff = einrueckZeit.Value - ausrueckZeit.Value;
                            dauer = FormatTimeSpan(diff < TimeSpan.Zero ? TimeSpan.Zero : diff);
                        }
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text(dauer).FontSize(9);

                        table.Cell().Element(c => RowCell(c, bg)).Text(team.Status).FontSize(9);

                        static IContainer RowCell(IContainer c, string bg) =>
                            c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
                    }
                });
            });
        }

        /// <summary>
        /// Komponiert die Grunddaten eines archivierten Einsatzes
        /// </summary>
        private void ComposeArchivedGrunddaten(IContainer container, ArchivedEinsatz einsatz)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, "Grunddaten"));

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(150);
                        columns.RelativeColumn();
                    });

                    AddTableRow(table, "Einsatznummer:", einsatz.EinsatzNummer);
                    AddTableRow(table, "Einsatztyp:", einsatz.EinsatzTyp);
                    AddTableRow(table, "Datum:", einsatz.EinsatzDatum.ToString("dd.MM.yyyy"));
                    
                    if (einsatz.AlarmierungsZeit.HasValue)
                    {
                        AddTableRow(table, "Alarmierung:", einsatz.AlarmierungsZeit.Value.ToString("HH:mm"));
                    }
                    
                    if (einsatz.EinsatzEnde.HasValue)
                    {
                        AddTableRow(table, "Einsatzende:", einsatz.EinsatzEnde.Value.ToString("HH:mm"));
                    }
                    
                    AddTableRow(table, "Dauer:", einsatz.DauerFormatiert);
                    AddTableRow(table, "Einsatzort:", einsatz.Einsatzort);
                    AddTableRow(table, "Alarmiert durch:", einsatz.Alarmiert);
                    AddTableRow(table, "Einsatzleiter:", einsatz.Einsatzleiter);
                    AddTableRow(table, "Führungsassistent:", einsatz.Fuehrungsassistent);
                });

                // Statistiken
                column.Item().PaddingTop(15).Row(row =>
                {
                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(col =>
                    {
                        col.Item().AlignCenter().Text($"{einsatz.AnzahlTeams}").FontSize(24).Bold();
                        col.Item().AlignCenter().Text("Teams").FontSize(10);
                    });
                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(col =>
                    {
                        col.Item().AlignCenter().Text($"{einsatz.AnzahlPersonal}").FontSize(24).Bold();
                        col.Item().AlignCenter().Text("Personal").FontSize(10);
                    });
                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(col =>
                    {
                        col.Item().AlignCenter().Text($"{einsatz.AnzahlHunde}").FontSize(24).Bold();
                        col.Item().AlignCenter().Text("Hunde").FontSize(10);
                    });
                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(col =>
                    {
                        col.Item().AlignCenter().Text($"{einsatz.AnzahlDrohnen}").FontSize(24).Bold();
                        col.Item().AlignCenter().Text("Drohnen").FontSize(10);
                    });
                });
            });
        }

        /// <summary>
        /// Komponiert das Ergebnis eines archivierten Einsatzes
        /// </summary>
        private void ComposeErgebnis(IContainer container, ArchivedEinsatz einsatz)
        {
            var ergebnisLower = (einsatz.Ergebnis ?? "").ToLowerInvariant();
            var bgColor = Colors.Grey.Lighten3;
            var textColor = Colors.Grey.Darken2;
            
            if (ergebnisLower.Contains("gefunden") || ergebnisLower.Contains("erfolg"))
            {
                bgColor = Colors.Green.Lighten3;
                textColor = Colors.Green.Darken2;
            }
            else if (ergebnisLower.Contains("abgebrochen") || ergebnisLower.Contains("erfolglos"))
            {
                bgColor = Colors.Orange.Lighten3;
                textColor = Colors.Orange.Darken2;
            }

            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, "Ergebnis"));

                column.Item().PaddingTop(10).Background(bgColor).Padding(15).Column(col =>
                {
                    col.Item().Text(einsatz.Ergebnis ?? "Kein Ergebnis angegeben")
                        .FontSize(14)
                        .Bold()
                        .FontColor(textColor);
                    
                    if (!string.IsNullOrEmpty(einsatz.Bemerkungen))
                    {
                        col.Item().PaddingTop(10).Text("Bemerkungen:")
                            .FontSize(10)
                            .Bold();
                        col.Item().Text(einsatz.Bemerkungen)
                            .FontSize(10);
                    }
                });
            });
        }

        private void ComposeVorOrtChecklisten(IContainer container, ArchivedEinsatz einsatz)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, "Vor-Ort Erfassung"));

                column.Item().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(col =>
                    {
                        col.Item().Text($"Personal vor Ort ({einsatz.PersonalNamen.Count})").Bold().FontSize(10);
                        if (einsatz.PersonalNamen.Count == 0)
                        {
                            col.Item().Text("Keine Eintraege").Italic().FontSize(9);
                        }
                        else
                        {
                            foreach (var personal in einsatz.PersonalNamen)
                            {
                                col.Item().Text($"- {personal}").FontSize(9);
                            }
                        }
                    });

                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(col =>
                    {
                        col.Item().Text($"Hunde vor Ort ({einsatz.HundeNamen.Count})").Bold().FontSize(10);
                        if (einsatz.HundeNamen.Count == 0)
                        {
                            col.Item().Text("Keine Eintraege").Italic().FontSize(9);
                        }
                        else
                        {
                            foreach (var hund in einsatz.HundeNamen)
                            {
                                col.Item().Text($"- {hund}").FontSize(9);
                            }
                        }
                    });
                });
            });
        }

        // ─────────────────────────────────────────────────────────────────────────
        // GPS-Tracks Sektion
        // ─────────────────────────────────────────────────────────────────────────

        private void ComposeGpsTracks(IContainer container, List<TeamTrackSnapshot> tracks)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, $"GPS-Tracks ({tracks.Count})"));

                // Übersichtstabelle
                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);      // Team
                        columns.RelativeColumn(1.5f);   // Halsband
                        columns.RelativeColumn(1.5f);   // Suchgebiet
                        columns.ConstantColumn(70);     // Strecke
                        columns.ConstantColumn(70);     // Dauer
                        columns.ConstantColumn(55);     // Punkte
                        columns.ConstantColumn(70);     // Erfasst
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HdrStyle).Text("Team").Bold();
                        header.Cell().Element(HdrStyle).Text("Halsband").Bold();
                        header.Cell().Element(HdrStyle).Text("Suchgebiet").Bold();
                        header.Cell().Element(HdrStyle).AlignCenter().Text("Strecke").Bold();
                        header.Cell().Element(HdrStyle).AlignCenter().Text("Dauer").Bold();
                        header.Cell().Element(HdrStyle).AlignCenter().Text("Punkte").Bold();
                        header.Cell().Element(HdrStyle).AlignCenter().Text("Erfasst").Bold();

                        static IContainer HdrStyle(IContainer c) =>
                            c.Background(Colors.Blue.Darken1).Padding(5);
                    });

                    var rowIndex = 0;
                    foreach (var track in tracks)
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Element(c => RowCell(c, bg)).Text(track.TeamName).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).Text(track.CollarName).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).Text(string.IsNullOrEmpty(track.SearchAreaName) ? "-" : track.SearchAreaName).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text(track.FormattedDistance).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text(track.FormattedDuration).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text($"{track.Points.Count}").FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text(track.CapturedAt.ToString("HH:mm")).FontSize(9);

                        static IContainer RowCell(IContainer c, string bg) =>
                            c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
                    }
                });

                // Grafische Darstellung jedes Tracks
                foreach (var track in tracks)
                {
                    if (track.Points.Count < 2) continue;

                    column.Item().PaddingTop(15).Column(trackCol =>
                    {
                        // Track-Header mit Farbindikator
                        trackCol.Item().Row(row =>
                        {
                            row.ConstantItem(14).Height(14).Svg(BuildColorDotSvg(track.Color));
                            row.AutoItem().PaddingLeft(6).AlignMiddle().Text($"{track.TeamName} — {track.CollarName}")
                                .FontSize(11).Bold();
                            row.RelativeItem().AlignRight().AlignMiddle().Text($"{track.FormattedDistance}  |  {track.FormattedDuration}")
                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                        });

                        // GPS-Track + Suchgebiet als Kartenbild (OSM-Tiles) oder SVG-Fallback
                        if (!string.IsNullOrEmpty(track.MapImageBase64))
                        {
                            var imageBytes = Convert.FromBase64String(track.MapImageBase64);
                            trackCol.Item().PaddingTop(5).Image(imageBytes).FitWidth();
                        }
                        else
                        {
                            trackCol.Item().PaddingTop(5).Height(280).ScaleToFit().Svg(BuildTrackSvg(track, 520, 300));
                        }

                        // Legende
                        trackCol.Item().PaddingTop(3).Row(row =>
                        {
                            row.AutoItem().Text("● Start").FontSize(7).FontColor("#28a745");
                            row.AutoItem().PaddingLeft(10).Text("● Ende").FontSize(7).FontColor("#dc3545");
                            if (track.SearchAreaCoordinates?.Count >= 3)
                            {
                                row.AutoItem().PaddingLeft(10).Text($"▪ Suchgebiet: {track.SearchAreaName}").FontSize(7)
                                    .FontColor(!string.IsNullOrEmpty(track.SearchAreaColor) ? track.SearchAreaColor : Colors.Blue.Medium);
                            }
                            row.RelativeItem();
                        });
                    });
                }
            });
        }

        private void ComposeArchivedGpsTrackMap(IContainer container, ArchivedEinsatz einsatz, List<TeamTrackSnapshot> tracks, byte[]? archivedTrackMapImage)
        {
            var validTracks = tracks.Where(track => track.Points.Count >= 2).ToList();
            if (validTracks.Count == 0)
                return;

            container.Column(column =>
            {
                if (archivedTrackMapImage != null)
                {
                    column.Item().Image(archivedTrackMapImage).FitWidth();
                    return;
                }

                column.Item().Height(430).ScaleToFit().Svg(BuildCombinedTrackSvg(validTracks, einsatz.ElwPosition, 760, 430));
            });
        }

        private static string BuildCombinedTrackSvg(List<TeamTrackSnapshot> tracks, (double Latitude, double Longitude)? elwPosition, float width, float height)
        {
            var validTracks = tracks.Where(track => track.Points.Count >= 2).ToList();
            if (validTracks.Count == 0)
                return $@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{width}"" height=""{height}"" viewBox=""0 0 {width} {height}""/>";

            var allLats = validTracks.SelectMany(track => track.Points.Select(point => point.Latitude)).ToList();
            var allLons = validTracks.SelectMany(track => track.Points.Select(point => point.Longitude)).ToList();

            foreach (var areaPoint in validTracks
                .Where(track => track.SearchAreaCoordinates?.Count >= 3)
                .SelectMany(track => track.SearchAreaCoordinates!))
            {
                allLats.Add(areaPoint.Latitude);
                allLons.Add(areaPoint.Longitude);
            }

            if (elwPosition.HasValue)
            {
                allLats.Add(elwPosition.Value.Latitude);
                allLons.Add(elwPosition.Value.Longitude);
            }

            var minLat = allLats.Min();
            var maxLat = allLats.Max();
            var minLon = allLons.Min();
            var maxLon = allLons.Max();

            var latPad = (maxLat - minLat) * 0.1;
            var lonPad = (maxLon - minLon) * 0.1;
            if (latPad < 0.0001) latPad = 0.0005;
            if (lonPad < 0.0001) lonPad = 0.0005;
            minLat -= latPad; maxLat += latPad;
            minLon -= lonPad; maxLon += lonPad;

            var latRange = maxLat - minLat;
            var lonRange = maxLon - minLon;

            var marginLeft = 20.0;
            var marginRight = 20.0;
            var marginTop = 20.0;
            var marginBottom = 20.0;
            var w = width - marginLeft - marginRight;
            var h = height - marginTop - marginBottom;

            var midLat = (minLat + maxLat) / 2.0;
            var lonScale = Math.Cos(midLat * Math.PI / 180);
            var effectiveLonRange = lonRange * lonScale;
            if (effectiveLonRange < 0.00001) effectiveLonRange = 0.001;

            var scaleX = w / effectiveLonRange;
            var scaleY = h / latRange;
            var scale = Math.Min(scaleX, scaleY);

            var offsetX = marginLeft + (w - effectiveLonRange * scale) / 2.0;
            var offsetY = marginTop + (h - latRange * scale) / 2.0;

            double ToX(double lon) => offsetX + (lon - minLon) * lonScale * scale;
            double ToY(double lat) => offsetY + (maxLat - lat) * scale;

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var svg = new System.Text.StringBuilder();
            svg.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{width}"" height=""{height}"" viewBox=""0 0 {width} {height}"">");
            svg.AppendLine($@"  <rect x=""0"" y=""0"" width=""{width}"" height=""{height}"" rx=""4"" fill=""#eef2e6"" stroke=""#b0b0b0"" stroke-width=""1""/>");

            var renderedAreas = new HashSet<string>(StringComparer.Ordinal);
            foreach (var track in validTracks.Where(track => track.SearchAreaCoordinates?.Count >= 3))
            {
                var areaKey = string.Join('|', track.SearchAreaCoordinates!.Select(coord => $"{coord.Latitude:F6},{coord.Longitude:F6}"));
                if (!renderedAreas.Add(areaKey))
                    continue;

                var areaColor = !string.IsNullOrEmpty(track.SearchAreaColor) ? track.SearchAreaColor : "#3388ff";
                var safeAreaColor = System.Security.SecurityElement.Escape(areaColor);
                var areaPoints = string.Join(" ", track.SearchAreaCoordinates!.Select(coord =>
                    $"{ToX(coord.Longitude).ToString("F1", inv)},{ToY(coord.Latitude).ToString("F1", inv)}"));
                svg.AppendLine($@"  <polygon points=""{areaPoints}"" fill=""{safeAreaColor}"" fill-opacity=""0.10"" stroke=""{safeAreaColor}"" stroke-width=""2"" stroke-dasharray=""8,4""/>");
            }

            foreach (var track in validTracks)
            {
                var polyPoints = string.Join(" ", track.Points.Select(point =>
                    $"{ToX(point.Longitude).ToString("F1", inv)},{ToY(point.Latitude).ToString("F1", inv)}"));
                var safeColor = System.Security.SecurityElement.Escape(track.Color);
                svg.AppendLine($@"  <polyline points=""{polyPoints}"" fill=""none"" stroke=""#00000030"" stroke-width=""4"" stroke-linecap=""round"" stroke-linejoin=""round""/>");
                svg.AppendLine($@"  <polyline points=""{polyPoints}"" fill=""none"" stroke=""{safeColor}"" stroke-width=""2.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>");
            }

            if (elwPosition.HasValue)
            {
                var elwX = ToX(elwPosition.Value.Longitude);
                var elwY = ToY(elwPosition.Value.Latitude);
                svg.AppendLine($@"  <circle cx=""{elwX.ToString("F1", inv)}"" cy=""{elwY.ToString("F1", inv)}"" r=""8"" fill=""#dc143c"" stroke=""white"" stroke-width=""2""/>");
                svg.AppendLine($@"  <text x=""{(elwX + 12).ToString("F1", inv)}"" y=""{(elwY + 4).ToString("F1", inv)}"" font-size=""9"" font-weight=""bold"" fill=""#dc143c"">ELW</text>");
            }

            svg.AppendLine("</svg>");
            return svg.ToString();
        }

        private static string BuildColorDotSvg(string hexColor)
        {
            return $@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""14"" height=""14"" viewBox=""0 0 14 14"">
  <circle cx=""7"" cy=""7"" r=""6"" fill=""{System.Security.SecurityElement.Escape(hexColor)}""/>
</svg>";
        }

        private static string BuildTrackSvg(TeamTrackSnapshot track, float width, float height)
        {
            var points = track.Points;
            if (points.Count < 2) return $@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{width}"" height=""{height}"" viewBox=""0 0 {width} {height}""/>";

            var hasArea = track.SearchAreaCoordinates?.Count >= 3;

            // Bounding Box: Track-Punkte + Suchgebiet-Koordinaten
            var allLats = points.Select(p => p.Latitude).ToList();
            var allLons = points.Select(p => p.Longitude).ToList();
            if (hasArea)
            {
                allLats.AddRange(track.SearchAreaCoordinates!.Select(c => c.Latitude));
                allLons.AddRange(track.SearchAreaCoordinates!.Select(c => c.Longitude));
            }

            var minLat = allLats.Min();
            var maxLat = allLats.Max();
            var minLon = allLons.Min();
            var maxLon = allLons.Max();

            // Etwas Rand hinzufügen (10% der Spanne)
            var latPad = (maxLat - minLat) * 0.1;
            var lonPad = (maxLon - minLon) * 0.1;
            if (latPad < 0.0001) latPad = 0.0005;
            if (lonPad < 0.0001) lonPad = 0.0005;
            minLat -= latPad; maxLat += latPad;
            minLon -= lonPad; maxLon += lonPad;

            var latRange = maxLat - minLat;
            var lonRange = maxLon - minLon;

            var marginLeft = 45.0;   // Platz für Y-Achse Labels
            var marginRight = 15.0;
            var marginTop = 15.0;
            var marginBottom = 30.0; // Platz für X-Achse Labels + Maßstab
            var w = width - marginLeft - marginRight;
            var h = height - marginTop - marginBottom;

            // Maintain aspect ratio using Mercator-like correction
            var midLat = (minLat + maxLat) / 2.0;
            var lonScale = Math.Cos(midLat * Math.PI / 180);
            var effectiveLonRange = lonRange * lonScale;
            if (effectiveLonRange < 0.00001) effectiveLonRange = 0.001;

            var scaleX = w / effectiveLonRange;
            var scaleY = h / latRange;
            var scale = Math.Min(scaleX, scaleY);

            var offsetX = marginLeft + (w - effectiveLonRange * scale) / 2.0;
            var offsetY = marginTop + (h - latRange * scale) / 2.0;

            double ToX(double lon) => offsetX + (lon - minLon) * lonScale * scale;
            double ToY(double lat) => offsetY + (maxLat - lat) * scale;

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var svg = new System.Text.StringBuilder();
            svg.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{width}"" height=""{height}"" viewBox=""0 0 {width} {height}"">");

            // Hintergrund
            svg.AppendLine($@"  <rect x=""0"" y=""0"" width=""{width}"" height=""{height}"" rx=""4"" fill=""#eef2e6"" stroke=""#b0b0b0"" stroke-width=""1""/>");

            // Koordinatengitter
            var gridLines = 5;
            for (int i = 0; i <= gridLines; i++)
            {
                // Horizontale Linien (Breitengrade)
                var lat = minLat + latRange * i / gridLines;
                var y = ToY(lat);
                svg.AppendLine($@"  <line x1=""{marginLeft.ToString("F0", inv)}"" y1=""{y.ToString("F1", inv)}"" x2=""{(width - marginRight).ToString("F0", inv)}"" y2=""{y.ToString("F1", inv)}"" stroke=""#c8d0b8"" stroke-width=""0.5""/>");
                svg.AppendLine($@"  <text x=""{(marginLeft - 3).ToString("F0", inv)}"" y=""{(y + 3).ToString("F1", inv)}"" font-size=""6.5"" fill=""#707070"" text-anchor=""end"">{lat.ToString("F4", inv)}°</text>");

                // Vertikale Linien (Längengrade)
                var lon = minLon + lonRange * i / gridLines;
                var x = ToX(lon);
                svg.AppendLine($@"  <line x1=""{x.ToString("F1", inv)}"" y1=""{marginTop.ToString("F0", inv)}"" x2=""{x.ToString("F1", inv)}"" y2=""{(height - marginBottom).ToString("F0", inv)}"" stroke=""#c8d0b8"" stroke-width=""0.5""/>");
                svg.AppendLine($@"  <text x=""{x.ToString("F1", inv)}"" y=""{(height - marginBottom + 10).ToString("F0", inv)}"" font-size=""6.5"" fill=""#707070"" text-anchor=""middle"">{lon.ToString("F4", inv)}°</text>");
            }

            // Suchgebiet-Polygon (halbtransparent mit Füllmuster)
            if (hasArea)
            {
                var areaColor = !string.IsNullOrEmpty(track.SearchAreaColor) ? track.SearchAreaColor : "#3388ff";
                var safeAreaColor = System.Security.SecurityElement.Escape(areaColor);
                var areaPoints = string.Join(" ", track.SearchAreaCoordinates!.Select(c =>
                    $"{ToX(c.Longitude).ToString("F1", inv)},{ToY(c.Latitude).ToString("F1", inv)}"));
                svg.AppendLine($@"  <polygon points=""{areaPoints}"" fill=""{safeAreaColor}"" fill-opacity=""0.12"" stroke=""{safeAreaColor}"" stroke-width=""2"" stroke-dasharray=""8,4""/>");

                // Suchgebiet-Label
                var cx = track.SearchAreaCoordinates!.Average(c => ToX(c.Longitude));
                var cy = track.SearchAreaCoordinates!.Average(c => ToY(c.Latitude));
                svg.AppendLine($@"  <text x=""{cx.ToString("F1", inv)}"" y=""{cy.ToString("F1", inv)}"" font-size=""9"" font-weight=""bold"" fill=""{safeAreaColor}"" text-anchor=""middle"" opacity=""0.5"">{System.Security.SecurityElement.Escape(track.SearchAreaName)}</text>");
            }

            // Track-Linie (mit Schatten für bessere Sichtbarkeit)
            var polyPoints = string.Join(" ", points.Select(p =>
                $"{ToX(p.Longitude).ToString("F1", inv)},{ToY(p.Latitude).ToString("F1", inv)}"));
            var safeColor = System.Security.SecurityElement.Escape(track.Color);
            svg.AppendLine($@"  <polyline points=""{polyPoints}"" fill=""none"" stroke=""#00000030"" stroke-width=""4"" stroke-linecap=""round"" stroke-linejoin=""round""/>");
            svg.AppendLine($@"  <polyline points=""{polyPoints}"" fill=""none"" stroke=""{safeColor}"" stroke-width=""2.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>");

            // Start-Marker
            var sx = ToX(points[0].Longitude);
            var sy = ToY(points[0].Latitude);
            svg.AppendLine($@"  <circle cx=""{sx.ToString("F1", inv)}"" cy=""{sy.ToString("F1", inv)}"" r=""7"" fill=""#28a745"" stroke=""white"" stroke-width=""2""/>");
            svg.AppendLine($@"  <text x=""{(sx + 10).ToString("F1", inv)}"" y=""{(sy + 4).ToString("F1", inv)}"" font-size=""9"" font-weight=""bold"" fill=""#28a745"">Start</text>");

            // Ende-Marker
            var ex2 = ToX(points[^1].Longitude);
            var ey = ToY(points[^1].Latitude);
            svg.AppendLine($@"  <circle cx=""{ex2.ToString("F1", inv)}"" cy=""{ey.ToString("F1", inv)}"" r=""7"" fill=""#dc3545"" stroke=""white"" stroke-width=""2""/>");
            svg.AppendLine($@"  <text x=""{(ex2 + 10).ToString("F1", inv)}"" y=""{(ey + 4).ToString("F1", inv)}"" font-size=""9"" font-weight=""bold"" fill=""#dc3545"">Ende</text>");

            // Maßstab (unten rechts)
            var scaleBarLon = lonRange * 0.25; // 25% der Kartenbreite
            var scaleBarMeters = scaleBarLon * lonScale * 111320; // Grad → Meter
            string scaleLabel;
            if (scaleBarMeters >= 1000)
                scaleLabel = $"{(scaleBarMeters / 1000.0).ToString("F1", inv)} km";
            else
                scaleLabel = $"{scaleBarMeters.ToString("F0", inv)} m";

            var sbX1 = width - marginRight - 10 - scaleBarLon * lonScale * scale;
            var sbX2 = width - marginRight - 10;
            var sbY2 = height - 8;
            svg.AppendLine($@"  <line x1=""{sbX1.ToString("F1", inv)}"" y1=""{sbY2.ToString("F0", inv)}"" x2=""{sbX2.ToString("F1", inv)}"" y2=""{sbY2.ToString("F0", inv)}"" stroke=""#404040"" stroke-width=""2""/>");
            svg.AppendLine($@"  <line x1=""{sbX1.ToString("F1", inv)}"" y1=""{(sbY2 - 4).ToString("F0", inv)}"" x2=""{sbX1.ToString("F1", inv)}"" y2=""{(sbY2 + 1).ToString("F0", inv)}"" stroke=""#404040"" stroke-width=""1.5""/>");
            svg.AppendLine($@"  <line x1=""{sbX2.ToString("F1", inv)}"" y1=""{(sbY2 - 4).ToString("F0", inv)}"" x2=""{sbX2.ToString("F1", inv)}"" y2=""{(sbY2 + 1).ToString("F0", inv)}"" stroke=""#404040"" stroke-width=""1.5""/>");
            var sbMid = ((sbX1 + sbX2) / 2).ToString("F1", inv);
            svg.AppendLine($@"  <text x=""{sbMid}"" y=""{(sbY2 - 5).ToString("F0", inv)}"" font-size=""7"" fill=""#404040"" text-anchor=""middle"">{scaleLabel}</text>");

            // Nordpfeil (oben rechts)
            var nX = width - marginRight - 14;
            var nY = marginTop + 6;
            svg.AppendLine($@"  <polygon points=""{nX.ToString("F0", inv)},{(nY + 16).ToString("F0", inv)} {(nX - 5).ToString("F0", inv)},{(nY + 16).ToString("F0", inv)} {nX.ToString("F0", inv)},{nY.ToString("F0", inv)}"" fill=""#404040""/>");
            svg.AppendLine($@"  <polygon points=""{nX.ToString("F0", inv)},{(nY + 16).ToString("F0", inv)} {(nX + 5).ToString("F0", inv)},{(nY + 16).ToString("F0", inv)} {nX.ToString("F0", inv)},{nY.ToString("F0", inv)}"" fill=""#a0a0a0""/>");
            svg.AppendLine($@"  <text x=""{nX.ToString("F0", inv)}"" y=""{(nY - 2).ToString("F0", inv)}"" font-size=""8"" font-weight=""bold"" fill=""#404040"" text-anchor=""middle"">N</text>");

            svg.AppendLine("</svg>");
            return svg.ToString();
        }

        private static (byte r, byte g, byte b) ParseHexColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 7)
                return (255, 68, 68); // default red

            try
            {
                hex = hex.TrimStart('#');
                return (
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16)
                );
            }
            catch
            {
                return (255, 68, 68);
            }
        }
    }
}

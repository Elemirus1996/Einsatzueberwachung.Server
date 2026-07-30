using Einsatzueberwachung.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class PdfExportService
    {
        private void ComposeCoverageAnalysis(IContainer container, List<SearchAreaCoverageResult> coverage, byte[]? heatmapImage)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, "Flächenabdeckung"));

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HdrStyle).Text("Suchgebiet").Bold();
                        header.Cell().Element(HdrStyle).AlignCenter().Text("Zellen gesamt").Bold();
                        header.Cell().Element(HdrStyle).AlignCenter().Text("Abgesucht").Bold();
                        header.Cell().Element(HdrStyle).AlignCenter().Text("Abdeckung").Bold();

                        static IContainer HdrStyle(IContainer c) =>
                            c.Background("#2C3E50").Padding(5).DefaultTextStyle(s => s.FontColor(Colors.White));
                    });

                    var rowIndex = 0;
                    foreach (var result in coverage)
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Element(c => RowCell(c, bg)).Text(result.SearchAreaName).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text($"{result.TotalCells}").FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text($"{result.CoveredCells}").FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter()
                            .Text($"{result.CoveragePercent:F0} %").FontSize(9).Bold()
                            .FontColor(result.CoveragePercent >= 80 ? "#27AE60" : result.CoveragePercent >= 40 ? "#E67E22" : "#C0392B");

                        static IContainer RowCell(IContainer c, string bg) =>
                            c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
                    }
                });

                column.Item().PaddingTop(4)
                    .Text($"Näherungsweise Berechnung: Rasterzelle gilt als abgesucht, wenn ein GPS-Punkt im Umkreis von {coverage.FirstOrDefault()?.CoverageRadiusMeters ?? 25:F0} m liegt.")
                    .FontSize(7).FontColor(Colors.Grey.Darken1).Italic();

                if (heatmapImage != null)
                {
                    column.Item().PaddingTop(10).Image(heatmapImage).FitWidth();
                }
                else
                {
                    column.Item().PaddingTop(10).Background(Colors.Grey.Lighten3).Height(200)
                        .AlignCenter().AlignMiddle()
                        .Text("Keine Heatmap-Karte verfügbar").FontSize(10).FontColor(Colors.Grey.Darken2);
                }
            });
        }
    }
}

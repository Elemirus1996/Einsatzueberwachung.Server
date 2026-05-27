using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class PdfExportService
    {
        private void ComposeGpsTracks(IContainer container, List<TeamTrackSnapshot> tracks)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, $"GPS-Tracks ({tracks.Count})"));

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(1.5f);
                        columns.ConstantColumn(70);
                        columns.ConstantColumn(70);
                        columns.ConstantColumn(55);
                        columns.ConstantColumn(70);
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
                            c.Background("#2C3E50").Padding(5).DefaultTextStyle(s => s.FontColor(Colors.White));
                    });

                    var rowIndex = 0;
                    foreach (var track in tracks)
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Element(c => RowCell(c, bg)).Text(track.TeamName).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).Text(track.CollarName).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).Text(string.IsNullOrWhiteSpace(track.SearchAreaName) ? "-" : track.SearchAreaName).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text(track.FormattedDistance).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text(track.FormattedDuration).FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text($"{track.Points.Count}").FontSize(9);
                        table.Cell().Element(c => RowCell(c, bg)).AlignCenter().Text(track.CapturedAt.ToString("HH:mm")).FontSize(9);

                        static IContainer RowCell(IContainer c, string bg) =>
                            c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
                    }
                });

                foreach (var track in tracks)
                {
                    if (track.Points.Count < 2) continue;

                    column.Item().PaddingTop(15).Column(trackCol =>
                    {
                        trackCol.Item().Row(row =>
                        {
                            row.ConstantItem(14).Height(14).Svg(BuildColorDotSvg(track.Color));
                            row.AutoItem().PaddingLeft(6).AlignMiddle().Text($"{track.TeamName} — {track.CollarName}")
                                .FontSize(11).Bold();
                            row.RelativeItem().AlignRight().AlignMiddle().Text($"{track.FormattedDistance}  |  {track.FormattedDuration}")
                                .FontSize(9).FontColor(Colors.Grey.Darken1);
                        });

                        if (!string.IsNullOrWhiteSpace(track.MapImageBase64))
                        {
                            var imageBytes = Convert.FromBase64String(track.MapImageBase64);
                            trackCol.Item().PaddingTop(5).Image(imageBytes).FitWidth();
                        }
                        else
                        {
                            trackCol.Item().PaddingTop(5).Height(280).ScaleToFit().Svg(BuildTrackSvg(track, 520, 300));
                        }

                        trackCol.Item().PaddingTop(3).Row(row =>
                        {
                            row.AutoItem().Text("● Start").FontSize(7).FontColor("#28a745");
                            row.AutoItem().PaddingLeft(10).Text("● Ende").FontSize(7).FontColor("#dc3545");
                            if (track.SearchAreaCoordinates?.Count >= 3)
                            {
                                row.AutoItem().PaddingLeft(10).Text($"▪ Suchgebiet: {track.SearchAreaName}").FontSize(7)
                                    .FontColor(!string.IsNullOrWhiteSpace(track.SearchAreaColor) ? track.SearchAreaColor : Colors.Blue.Medium);
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

        public async Task<byte[]> ExportEinsatzKarteToPdfBytesAsync(
            EinsatzData einsatzData,
            List<Team> teams,
            MapTileType tileType = MapTileType.Streets,
            string? filterTeamId = null)
        {
            var staffelInfo = await ResolveStaffelInfoAsync(einsatzData);

            var allAreas = einsatzData.SearchAreas ?? new List<SearchArea>();
            var filteredAreas = string.IsNullOrWhiteSpace(filterTeamId)
                ? allAreas
                : allAreas.Where(a => a.AssignedTeamId == filterTeamId ||
                    (!string.IsNullOrWhiteSpace(a.AssignedTeamName) &&
                     teams.Any(t => t.TeamId == filterTeamId &&
                                    string.Equals(t.TeamName, a.AssignedTeamName, StringComparison.OrdinalIgnoreCase))))
                          .ToList();

            var filteredTeams = string.IsNullOrWhiteSpace(filterTeamId)
                ? teams
                : teams.Where(t => t.TeamId == filterTeamId).ToList();

            byte[]? mapBytes = null;
            if (_mapRenderer != null)
            {
                mapBytes = await _mapRenderer.RenderSearchAreaMapAsync(filteredAreas, einsatzData.ElwPosition, tileType);
            }

            var capturedMapBytes = mapBytes;
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream();
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.MarginTop(4, Unit.Millimetre);
                        page.MarginBottom(6, Unit.Millimetre);
                        page.MarginHorizontal(10, Unit.Millimetre);
                        page.PageColor(Colors.White);

                        page.Header().PaddingBottom(3)
                            .Element(c => ComposeKarteHeader(c, einsatzData, staffelInfo));

                        page.Content().Element(c =>
                        {
                            if (capturedMapBytes != null)
                            {
                                c.Image(capturedMapBytes).FitArea();
                            }
                            else
                            {
                                c.Background(Colors.Grey.Lighten3)
                                 .AlignCenter().AlignMiddle()
                                 .Text("Keine Suchgebiete oder ELW-Position vorhanden")
                                 .FontSize(14).FontColor(Colors.Grey.Darken2);
                            }
                        });
                    });

                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(12, Unit.Millimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Content().Element(c => ComposeKarteListe(c, einsatzData, filteredAreas, filteredTeams));
                    });
                }).GeneratePdf(stream);

                return stream.ToArray();
            });
        }

        public async Task<byte[]> ExportEinsatzKarteToPdfBytesAsync(
            EinsatzData einsatzData,
            List<Team> teams,
            MapPrintOptions options,
            List<TeamTrackSnapshot>? gpsTracks = null,
            Dictionary<string, List<TeamPhoneLocation>>? phoneTrackHistories = null)
        {
            var staffelInfo = await ResolveStaffelInfoAsync(einsatzData);

            var allAreas = einsatzData.SearchAreas ?? new List<SearchArea>();
            var filteredAreas = string.IsNullOrWhiteSpace(options.FilterTeamId)
                ? allAreas
                : allAreas.Where(a => a.AssignedTeamId == options.FilterTeamId ||
                    (!string.IsNullOrWhiteSpace(a.AssignedTeamName) &&
                     teams.Any(t => t.TeamId == options.FilterTeamId &&
                                    string.Equals(t.TeamName, a.AssignedTeamName, StringComparison.OrdinalIgnoreCase))))
                          .ToList();

            var filteredTeams = string.IsNullOrWhiteSpace(options.FilterTeamId)
                ? teams
                : teams.Where(t => t.TeamId == options.FilterTeamId).ToList();

            var markers = options.ShowPointMarkers ? (einsatzData.MapMarkers ?? new List<MapMarker>()) : null;

            byte[]? mapBytes = null;
            if (_mapRenderer != null)
            {
                mapBytes = await _mapRenderer.RenderSearchAreaMapAsync(
                    filteredAreas, einsatzData.ElwPosition, options,
                    markers, gpsTracks, phoneTrackHistories, teams);
            }

            var capturedMapBytes = mapBytes;
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream();
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.MarginTop(4, Unit.Millimetre);
                        page.MarginBottom(6, Unit.Millimetre);
                        page.MarginHorizontal(10, Unit.Millimetre);
                        page.PageColor(Colors.White);

                        page.Header().PaddingBottom(3)
                            .Element(c => ComposeKarteHeader(c, einsatzData, staffelInfo));

                        page.Content().Element(c =>
                        {
                            if (capturedMapBytes != null)
                            {
                                c.Image(capturedMapBytes).FitArea();
                            }
                            else
                            {
                                c.Background(Colors.Grey.Lighten3)
                                 .AlignCenter().AlignMiddle()
                                 .Text("Keine Suchgebiete oder ELW-Position vorhanden")
                                 .FontSize(14).FontColor(Colors.Grey.Darken2);
                            }
                        });
                    });

                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(12, Unit.Millimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(10));

                        page.Content().Element(c => ComposeKarteListe(c, einsatzData, filteredAreas, filteredTeams));
                    });
                }).GeneratePdf(stream);

                return stream.ToArray();
            });
        }

        private static void ComposeKarteHeader(IContainer container, EinsatzData einsatzData, StaffelInfo staffelInfo)
        {
            container.Background("#2C3E50").Padding(6).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        if (!string.IsNullOrWhiteSpace(staffelInfo.Name))
                        {
                            r.AutoItem().Text(staffelInfo.Name).FontSize(9).FontColor(Colors.White).Italic();
                            r.AutoItem().Text("  |  ").FontSize(9).FontColor(Colors.Grey.Lighten2);
                        }
                        if (!string.IsNullOrWhiteSpace(einsatzData.EinsatzNummer))
                        {
                            r.AutoItem().Text($"Einsatz {einsatzData.EinsatzNummer}").FontSize(9).Bold().FontColor(Colors.White);
                            r.AutoItem().Text("  |  ").FontSize(9).FontColor(Colors.Grey.Lighten2);
                        }
                        if (!string.IsNullOrWhiteSpace(einsatzData.Einsatzort))
                        {
                            r.AutoItem().Text(einsatzData.Einsatzort).FontSize(9).FontColor(Colors.White);
                            r.AutoItem().Text("  |  ").FontSize(9).FontColor(Colors.Grey.Lighten2);
                        }
                        if (!string.IsNullOrWhiteSpace(einsatzData.Einsatzleiter))
                        {
                            r.AutoItem().Text($"EL: {einsatzData.Einsatzleiter}").FontSize(9).FontColor(Colors.White);
                        }
                    });
                });

                row.AutoItem().AlignRight().Column(col =>
                {
                    col.Item().AlignRight().Text("EINSATZKARTE").FontSize(11).Bold().FontColor(Colors.White);
                    col.Item().AlignRight().Text($"Stand: {DateTime.Now:dd.MM.yyyy HH:mm} Uhr").FontSize(8).FontColor(Colors.Grey.Lighten2);
                });
            });
        }

        private static void ComposeKarteListe(IContainer container, EinsatzData einsatzData, List<SearchArea> searchAreas, List<Team> teams)
        {
            container.Column(col =>
            {
                col.Item().Element(c => ComposeSectionHeader(c, $"Suchgebietszuweisung — Einsatz {einsatzData.EinsatzNummer}"));
                col.Item().Height(8);

                var areas = searchAreas.OrderBy(a => a.Name).ToList();

                if (areas.Count == 0)
                {
                    col.Item().Padding(20)
                       .AlignCenter().Text("Keine Suchgebiete angelegt.")
                       .FontSize(12).FontColor(Colors.Grey.Darken1).Italic();
                    return;
                }

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(28);
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2.5f);
                        columns.RelativeColumn(2.5f);
                        columns.ConstantColumn(70);
                    });

                    table.Header(header =>
                    {
                        static IContainer HeaderCell(IContainer c) =>
                            c.Background("#2C3E50").Padding(6).DefaultTextStyle(s => s.FontColor(Colors.White).Bold());

                        header.Cell().Element(HeaderCell).AlignCenter().Text("Nr.");
                        header.Cell().Element(HeaderCell).Text("Suchgebiet");
                        header.Cell().Element(HeaderCell).Text("Team");
                        header.Cell().Element(HeaderCell).Text("Hund");
                        header.Cell().Element(HeaderCell).Text("Halsband");
                        header.Cell().Element(HeaderCell).AlignCenter().Text("Status");
                    });

                    var rowIndex = 0;
                    foreach (var area in areas)
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                        var team = ResolveTeamForArea(area, teams);
                        var dogName = string.IsNullOrWhiteSpace(team?.DogName) ? "-" : team.DogName;
                        var collarName = ResolveCollarName(team);

                        static IContainer DataCell(IContainer c, string bg) =>
                            c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(7).PaddingHorizontal(5);

                        table.Cell().Element(c => DataCell(c, bg)).AlignCenter().Text($"{rowIndex}").FontSize(10);

                        table.Cell().Element(c => DataCell(c, bg)).Row(r =>
                        {
                            r.ConstantItem(14).Height(14).AlignMiddle()
                             .Background(string.IsNullOrWhiteSpace(area.Color) ? "#2196F3" : area.Color);
                            r.ConstantItem(6);
                            r.RelativeItem().AlignMiddle().Text(area.Name ?? "-").Bold();
                        });

                        table.Cell().Element(c => DataCell(c, bg))
                             .Text(string.IsNullOrWhiteSpace(area.AssignedTeamName) ? "-" : area.AssignedTeamName);

                        table.Cell().Element(c => DataCell(c, bg)).Text(dogName);
                        table.Cell().Element(c => DataCell(c, bg)).Text(collarName);

                        table.Cell().Element(c => DataCell(c, bg)).AlignCenter()
                             .Text(area.IsCompleted ? "✓ Fertig" : "— Offen")
                             .FontColor(area.IsCompleted ? "#27AE60" : "#7F8C8D");
                    }
                });

                col.Item().PaddingTop(10).Text($"Erstellt: {DateTime.Now:dd.MM.yyyy HH:mm} Uhr  |  {areas.Count} Suchgebiet{(areas.Count == 1 ? "" : "e")}")
                   .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        }

        private static Team? ResolveTeamForArea(SearchArea area, List<Team> teams)
        {
            if (!string.IsNullOrWhiteSpace(area.AssignedTeamId))
                return teams.FirstOrDefault(t => t.TeamId == area.AssignedTeamId);
            if (!string.IsNullOrWhiteSpace(area.AssignedTeamName))
                return teams.FirstOrDefault(t => string.Equals(t.TeamName, area.AssignedTeamName, StringComparison.OrdinalIgnoreCase));
            return null;
        }

        private static string ResolveCollarName(Team? team)
        {
            if (team == null) return "-";
            if (!string.IsNullOrWhiteSpace(team.CollarName) && !string.IsNullOrWhiteSpace(team.CollarId))
                return $"{team.CollarName} [{team.CollarId}]";
            if (!string.IsNullOrWhiteSpace(team.CollarName))
                return team.CollarName;
            return string.IsNullOrWhiteSpace(team.CollarId) ? "-" : team.CollarId;
        }

        private static string BuildColorDotSvg(string hexColor)
        {
            return $@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""14"" height=""14"" viewBox=""0 0 14 14"">
  <circle cx=""7"" cy=""7"" r=""6"" fill=""{System.Security.SecurityElement.Escape(hexColor)}""/>
</svg>";
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

                var areaColor = !string.IsNullOrWhiteSpace(track.SearchAreaColor) ? track.SearchAreaColor : "#3388ff";
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

        private static string BuildTrackSvg(TeamTrackSnapshot track, float width, float height)
        {
            var points = track.Points;
            if (points.Count < 2) return $@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{width}"" height=""{height}"" viewBox=""0 0 {width} {height}""/>";

            var hasArea = track.SearchAreaCoordinates?.Count >= 3;

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

            var latPad = (maxLat - minLat) * 0.1;
            var lonPad = (maxLon - minLon) * 0.1;
            if (latPad < 0.0001) latPad = 0.0005;
            if (lonPad < 0.0001) lonPad = 0.0005;
            minLat -= latPad; maxLat += latPad;
            minLon -= lonPad; maxLon += lonPad;

            var latRange = maxLat - minLat;
            var lonRange = maxLon - minLon;

            var marginLeft = 45.0;
            var marginRight = 15.0;
            var marginTop = 15.0;
            var marginBottom = 30.0;
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

            var gridLines = 5;
            for (int i = 0; i <= gridLines; i++)
            {
                var lat = minLat + latRange * i / gridLines;
                var y = ToY(lat);
                svg.AppendLine($@"  <line x1=""{marginLeft.ToString("F0", inv)}"" y1=""{y.ToString("F1", inv)}"" x2=""{(width - marginRight).ToString("F0", inv)}"" y2=""{y.ToString("F1", inv)}"" stroke=""#c8d0b8"" stroke-width=""0.5""/>");
                svg.AppendLine($@"  <text x=""{(marginLeft - 3).ToString("F0", inv)}"" y=""{(y + 3).ToString("F1", inv)}"" font-size=""6.5"" fill=""#707070"" text-anchor=""end"">{lat.ToString("F4", inv)}°</text>");

                var lon = minLon + lonRange * i / gridLines;
                var x = ToX(lon);
                svg.AppendLine($@"  <line x1=""{x.ToString("F1", inv)}"" y1=""{marginTop.ToString("F0", inv)}"" x2=""{x.ToString("F1", inv)}"" y2=""{(height - marginBottom).ToString("F0", inv)}"" stroke=""#c8d0b8"" stroke-width=""0.5""/>");
                svg.AppendLine($@"  <text x=""{x.ToString("F1", inv)}"" y=""{(height - marginBottom + 10).ToString("F0", inv)}"" font-size=""6.5"" fill=""#707070"" text-anchor=""middle"">{lon.ToString("F4", inv)}°</text>");
            }

            if (hasArea)
            {
                var areaColor = !string.IsNullOrWhiteSpace(track.SearchAreaColor) ? track.SearchAreaColor : "#3388ff";
                var safeAreaColor = System.Security.SecurityElement.Escape(areaColor);
                var areaPoints = string.Join(" ", track.SearchAreaCoordinates!.Select(c =>
                    $"{ToX(c.Longitude).ToString("F1", inv)},{ToY(c.Latitude).ToString("F1", inv)}"));
                svg.AppendLine($@"  <polygon points=""{areaPoints}"" fill=""{safeAreaColor}"" fill-opacity=""0.12"" stroke=""{safeAreaColor}"" stroke-width=""2"" stroke-dasharray=""8,4""/>");

                var cx = track.SearchAreaCoordinates!.Average(c => ToX(c.Longitude));
                var cy = track.SearchAreaCoordinates!.Average(c => ToY(c.Latitude));
                svg.AppendLine($@"  <text x=""{cx.ToString("F1", inv)}"" y=""{cy.ToString("F1", inv)}"" font-size=""9"" font-weight=""bold"" fill=""{safeAreaColor}"" text-anchor=""middle"" opacity=""0.5"">{System.Security.SecurityElement.Escape(track.SearchAreaName)}</text>");
            }

            var polyPoints = string.Join(" ", points.Select(p =>
                $"{ToX(p.Longitude).ToString("F1", inv)},{ToY(p.Latitude).ToString("F1", inv)}"));
            var safeColor = System.Security.SecurityElement.Escape(track.Color);
            svg.AppendLine($@"  <polyline points=""{polyPoints}"" fill=""none"" stroke=""#00000030"" stroke-width=""4"" stroke-linecap=""round"" stroke-linejoin=""round""/>");
            svg.AppendLine($@"  <polyline points=""{polyPoints}"" fill=""none"" stroke=""{safeColor}"" stroke-width=""2.5"" stroke-linecap=""round"" stroke-linejoin=""round""/>");

            var sx = ToX(points[0].Longitude);
            var sy = ToY(points[0].Latitude);
            svg.AppendLine($@"  <circle cx=""{sx.ToString("F1", inv)}"" cy=""{sy.ToString("F1", inv)}"" r=""7"" fill=""#28a745"" stroke=""white"" stroke-width=""2""/>");
            svg.AppendLine($@"  <text x=""{(sx + 10).ToString("F1", inv)}"" y=""{(sy + 4).ToString("F1", inv)}"" font-size=""9"" font-weight=""bold"" fill=""#28a745"">Start</text>");

            var ex2 = ToX(points[^1].Longitude);
            var ey = ToY(points[^1].Latitude);
            svg.AppendLine($@"  <circle cx=""{ex2.ToString("F1", inv)}"" cy=""{ey.ToString("F1", inv)}"" r=""7"" fill=""#dc3545"" stroke=""white"" stroke-width=""2""/>");
            svg.AppendLine($@"  <text x=""{(ex2 + 10).ToString("F1", inv)}"" y=""{(ey + 4).ToString("F1", inv)}"" font-size=""9"" font-weight=""bold"" fill=""#dc3545"">Ende</text>");

            var scaleBarLon = lonRange * 0.25;
            var scaleBarMeters = scaleBarLon * lonScale * 111320;
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

            var nX = width - marginRight - 14;
            var nY = marginTop + 6;
            svg.AppendLine($@"  <polygon points=""{nX.ToString("F0", inv)},{(nY + 16).ToString("F0", inv)} {(nX - 5).ToString("F0", inv)},{(nY + 16).ToString("F0", inv)} {nX.ToString("F0", inv)},{nY.ToString("F0", inv)}"" fill=""#404040""/>");
            svg.AppendLine($@"  <polygon points=""{nX.ToString("F0", inv)},{(nY + 16).ToString("F0", inv)} {(nX + 5).ToString("F0", inv)},{(nY + 16).ToString("F0", inv)} {nX.ToString("F0", inv)},{nY.ToString("F0", inv)}"" fill=""#a0a0a0""/>");
            svg.AppendLine($@"  <text x=""{nX.ToString("F0", inv)}"" y=""{(nY - 2).ToString("F0", inv)}"" font-size=""8"" font-weight=""bold"" fill=""#404040"" text-anchor=""middle"">N</text>");

            svg.AppendLine("</svg>");
            return svg.ToString();
        }
    }
}

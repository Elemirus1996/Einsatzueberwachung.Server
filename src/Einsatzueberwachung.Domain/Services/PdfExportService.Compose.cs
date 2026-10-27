using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class PdfExportService
    {
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
                    if (einsatzData.Szenario != EinsatzSzenarioType.Unbestimmt)
                        AddTableRow(table, "Szenario:", einsatzData.Szenario.GetDisplayName());
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
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1.5f);
                        columns.ConstantColumn(55);
                        columns.ConstantColumn(60);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellStyleHeader).Text("Team").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Hund / Drohne").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Personal").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Suchgebiet").Bold();
                        header.Cell().Element(CellStyleHeader).AlignCenter().Text("Ausrücken").Bold();
                        header.Cell().Element(CellStyleHeader).AlignCenter().Text("Einsatzzeit").Bold();

                        static IContainer CellStyleHeader(IContainer c) =>
                            c.Background("#2C3E50").Padding(5).DefaultTextStyle(s => s.FontColor(Colors.White));
                    });

                    var rowIndex = 0;
                    foreach (var team in teams)
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Element(c => CellStyle(c, bg)).Text(team.TeamName);

                        table.Cell().Element(c => CellStyle(c, bg)).Column(col =>
                        {
                            if (team.IsDroneTeam)
                                col.Item().Text($"Drohne: {team.DroneType}").FontSize(9);
                            else if (!string.IsNullOrWhiteSpace(team.DogName))
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
                            var helferText = team.HelferNamesJoined;
                            if (!string.IsNullOrWhiteSpace(helferText))
                                col.Item().Text($"Helfer: {helferText}").FontSize(8).Italic();
                        });

                        table.Cell().Element(c => CellStyle(c, bg)).Text(team.SearchAreaName ?? "-").FontSize(9);

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
                                col.Item().Text("KRITISCH").FontSize(7).Bold().FontColor("#C0392B");
                            else if (team.IsFirstWarning)
                                col.Item().Text("Warnung").FontSize(7).FontColor("#E67E22");
                        });

                        static IContainer CellStyle(IContainer c, string bg) =>
                            c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
                    }
                });
            });
        }

        private void ComposeVermisste(IContainer container, List<VermisstenInfo> vermisste)
        {
            container.Column(column =>
            {
                var title = vermisste.Count == 1 ? "Vermisste Person" : $"Vermisste Personen ({vermisste.Count})";
                column.Item().Element(c => ComposeSectionHeader(c, title));

                for (var i = 0; i < vermisste.Count; i++)
                {
                    var v = vermisste[i];
                    var idx = i + 1;
                    var personLabel = vermisste.Count > 1
                        ? $"Person {idx}: {(string.IsNullOrWhiteSpace(v.VollerName) || v.VollerName == "Unbekannt" ? "ohne Name" : v.VollerName)}"
                        : v.VollerName;

                    column.Item().PaddingTop(idx == 1 ? 10 : 14)
                        .Background("#F4F6F9").Padding(6)
                        .Text(personLabel).Bold().FontSize(11).FontColor("#2C3E50");

                    column.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(150);
                            columns.RelativeColumn();
                        });

                        if (!string.IsNullOrWhiteSpace(v.Alter))
                            AddTableRow(table, "Alter:", v.Alter);
                        if (!string.IsNullOrWhiteSpace(v.Geburtsdatum))
                            AddTableRow(table, "Geburtsdatum:", v.Geburtsdatum);
                        if (!string.IsNullOrWhiteSpace(v.Kleidung))
                            AddTableRow(table, "Kleidung:", v.Kleidung);
                        if (!string.IsNullOrWhiteSpace(v.Besonderheiten))
                            AddTableRow(table, "Besonderheiten:", v.Besonderheiten);
                        if (!string.IsNullOrWhiteSpace(v.ZuletztGesehenOrt))
                            AddTableRow(table, "Zuletzt gesehen:", v.ZuletztGesehenOrt);
                        if (!string.IsNullOrWhiteSpace(v.ZuletztGesehenZeit))
                            AddTableRow(table, "Uhrzeit:", v.ZuletztGesehenZeit);
                        if (!string.IsNullOrWhiteSpace(v.ZuletztGesehenVon))
                            AddTableRow(table, "Gesehen von:", v.ZuletztGesehenVon);
                        if (v.Orientierung != OrientierungsStatus.Unbekannt)
                            AddTableRow(table, "Orientierung:", v.Orientierung.ToString());
                        if (v.Mobilitaet != MobilitaetsStatus.Unbekannt)
                            AddTableRow(table, "Mobilität:", v.Mobilitaet.ToString());
                        if (v.Suizidrisiko == RisikoStatus.Ja)
                            AddTableRow(table, "Suizidrisiko:", "Ja");
                        if (v.Bewaffnet == RisikoStatus.Ja)
                            AddTableRow(table, "Bewaffnet:", "Ja");
                        if (!string.IsNullOrWhiteSpace(v.Vorerkrankungen))
                            AddTableRow(table, "Vorerkrankungen:", v.Vorerkrankungen);
                        if (!string.IsNullOrWhiteSpace(v.Medikamente))
                            AddTableRow(table, "Medikamente:", v.Medikamente);
                        if (!string.IsNullOrWhiteSpace(v.PolizeiKontaktName))
                        {
                            var polizei = v.PolizeiKontaktName;
                            if (!string.IsNullOrWhiteSpace(v.PolizeiTelefon))
                                polizei += $" · {v.PolizeiTelefon}";
                            if (!string.IsNullOrWhiteSpace(v.PolizeiDienstnummer))
                                polizei += $" (Dienstnr. {v.PolizeiDienstnummer})";
                            AddTableRow(table, "Polizei-Kontakt:", polizei);
                        }
                        if (!string.IsNullOrWhiteSpace(v.BosEinheit))
                            AddTableRow(table, "BOS-Einheit:", v.BosEinheit);
                    });

                    if (v.Checkliste is not null && v.Checkliste.Items.Count > 0)
                    {
                        column.Item().PaddingTop(6).Text("Checkliste").SemiBold().FontSize(10).FontColor("#2C3E50");
                        column.Item().PaddingTop(2).Table(checkTable =>
                        {
                            checkTable.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(2);
                            });

                            foreach (var item in v.Checkliste.Items)
                            {
                                v.Checkliste.Values.TryGetValue(item.Id.ToString(), out var raw);
                                string display;
                                if (item.Type == ChecklistItemType.Bool)
                                {
                                    display = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ? "✓ ja" : "—";
                                }
                                else
                                {
                                    display = string.IsNullOrWhiteSpace(raw) ? "—" : raw!;
                                }

                                AddTableRow(checkTable, item.Label + ":", display);
                            }
                        });
                    }
                }
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

                    table.Header(header =>
                    {
                        header.Cell().Element(CellStyleHeader).Text("Name").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Team").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Status").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Kartenlayout").Bold();
                        header.Cell().Element(CellStyleHeader).Text("Notizen").Bold();

                        static IContainer CellStyleHeader(IContainer c) =>
                            c.Background("#2C3E50").Padding(5).DefaultTextStyle(s => s.FontColor(Colors.White));
                    });

                    var rowIndex = 0;
                    foreach (var area in searchAreas)
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Element(c => CellStyle(c, bg)).Text(area.Name);
                        table.Cell().Element(c => CellStyle(c, bg)).Text(area.AssignedTeamName ?? "-");
                        table.Cell().Element(c => CellStyle(c, bg))
                            .Text(area.IsCompleted ? "✓ Abgeschlossen" : "— In Bearbeitung")
                            .FontColor(area.IsCompleted ? "#27AE60" : "#7F8C8D");
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
                        columns.ConstantColumn(96);
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(90);
                        columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HdrStyle).Text("Zeit").Bold();
                        header.Cell().Element(HdrStyle).Text("Typ").Bold();
                        header.Cell().Element(HdrStyle).Text("Quelle").Bold();
                        header.Cell().Element(HdrStyle).Text("Text").Bold();

                        static IContainer HdrStyle(IContainer c) =>
                            c.Background("#2C3E50").Padding(5).DefaultTextStyle(s => s.FontColor(Colors.White));
                    });

                    var rowIndex = 0;
                    foreach (var note in notes.OrderBy(n => n.Timestamp))
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                        var typeColor = GetNoteTypeColor(note.Type);

                        table.Cell().Element(c => RowCell(c, bg))
                            .AlignCenter().Text($"{note.Timestamp:dd.MM.\nHH:mm}").FontSize(8);

                        table.Cell().Element(c => c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4))
                            .Background(typeColor).Padding(3).AlignCenter()
                            .Text(GetNoteTypeLabel(note.Type)).FontSize(8).Bold().FontColor(Colors.White);

                        table.Cell().Element(c => RowCell(c, bg))
                            .AlignCenter().Text(note.SourceTeamName ?? "-").FontSize(8).Italic();

                        table.Cell().Element(c => RowCell(c, bg))
                            .Text(note.Text).FontSize(9);

                        static IContainer RowCell(IContainer c, string bg) =>
                            c.Background(bg).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
                    }
                });
            });
        }

        private static void ComposeZusammenfassung(IContainer container,
            int anzahlTeams, int anzahlSuchgebiete, int anzahlNotizen,
            string gesamtdauer, string? ergebnis)
        {
            container.Background("#F4F6F9").Border(1).BorderColor("#DDE1E7").Padding(12).Column(col =>
            {
                col.Item().Text("Zusammenfassung").FontSize(10).Bold().FontColor("#2C3E50").LetterSpacing(0.05f);
                col.Item().PaddingTop(8).Row(row =>
                {
                    StatBox(row.RelativeItem(), $"{anzahlTeams}", "Teams");
                    row.ConstantItem(6);
                    StatBox(row.RelativeItem(), $"{anzahlSuchgebiete}", "Suchgebiete");
                    row.ConstantItem(6);
                    StatBox(row.RelativeItem(), $"{anzahlNotizen}", "Notizen / Funk");
                    row.ConstantItem(6);
                    StatBox(row.RelativeItem(),
                        !string.IsNullOrWhiteSpace(ergebnis) ? ergebnis! : gesamtdauer,
                        !string.IsNullOrWhiteSpace(ergebnis) ? "Ergebnis" : "Gesamtdauer");
                });
            });

            static void StatBox(IContainer c, string value, string label)
            {
                c.Background(Colors.White).Border(1).BorderColor("#DDE1E7").Padding(10).Column(inner =>
                {
                    inner.Item().AlignCenter().Text(value).FontSize(18).Bold().FontColor("#2C3E50");
                    inner.Item().AlignCenter().Text(label).FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            }
        }

        private static void ComposeSectionHeader(IContainer container, string title, string? iconText = null)
        {
            container.Background("#2C3E50").Padding(8).Row(row =>
            {
                if (!string.IsNullOrWhiteSpace(iconText))
                    row.AutoItem().PaddingRight(8).Text(iconText).FontSize(13).FontColor(Colors.White).Bold();
                row.RelativeItem().Text(title).FontSize(13).Bold().FontColor(Colors.White);
            });
        }

        private void ComposeReportHeader(IContainer container, string title, string titleColor, StaffelInfo staffelInfo)
        {
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

        private void ComposeArchivedTeams(IContainer container, List<ArchivedTeam> teams, List<GlobalNotesEntry> notes)
        {
            container.Column(column =>
            {
                column.Item().Element(c => ComposeSectionHeader(c, $"Teams ({teams.Count})"));

                column.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2.5f);
                        columns.ConstantColumn(55);
                        columns.ConstantColumn(55);
                        columns.ConstantColumn(60);
                        columns.RelativeColumn(1.5f);
                    });

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
                            c.Background("#2C3E50").Padding(5).DefaultTextStyle(s => s.FontColor(Colors.White));
                    });

                    var rowIndex = 0;
                    foreach (var team in teams)
                    {
                        var bg = rowIndex++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                        table.Cell().Element(c => RowCell(c, bg)).Text(team.TeamName);

                        table.Cell().Element(c => RowCell(c, bg)).Column(col =>
                        {
                            if (!string.IsNullOrWhiteSpace(team.DroneName))
                                col.Item().Text($"Drohne: {team.DroneName}").FontSize(9);
                            else if (!string.IsNullOrWhiteSpace(team.DogName))
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
                    if (einsatz.Szenario != EinsatzSzenarioType.Unbestimmt)
                        AddTableRow(table, "Szenario:", einsatz.Szenario.GetDisplayName());
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

                column.Item().PaddingTop(15).Row(row =>
                {
                    row.RelativeItem().Background("#F4F6F9").Border(1).BorderColor("#DDE1E7").Padding(10).Column(col =>
                    {
                        col.Item().AlignCenter().Text($"{einsatz.AnzahlTeams}").FontSize(24).Bold().FontColor("#2C3E50");
                        col.Item().AlignCenter().Text("Teams").FontSize(10).FontColor(Colors.Grey.Darken1);
                    });
                    row.RelativeItem().Background("#F4F6F9").Border(1).BorderColor("#DDE1E7").Padding(10).Column(col =>
                    {
                        col.Item().AlignCenter().Text($"{einsatz.AnzahlPersonal}").FontSize(24).Bold().FontColor("#2C3E50");
                        col.Item().AlignCenter().Text("Personal").FontSize(10).FontColor(Colors.Grey.Darken1);
                    });
                    row.RelativeItem().Background("#F4F6F9").Border(1).BorderColor("#DDE1E7").Padding(10).Column(col =>
                    {
                        col.Item().AlignCenter().Text($"{einsatz.AnzahlHunde}").FontSize(24).Bold().FontColor("#2C3E50");
                        col.Item().AlignCenter().Text("Hunde").FontSize(10).FontColor(Colors.Grey.Darken1);
                    });
                    row.RelativeItem().Background("#F4F6F9").Border(1).BorderColor("#DDE1E7").Padding(10).Column(col =>
                    {
                        col.Item().AlignCenter().Text($"{einsatz.AnzahlDrohnen}").FontSize(24).Bold().FontColor("#2C3E50");
                        col.Item().AlignCenter().Text("Drohnen").FontSize(10).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }

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
                    col.Item().Text(string.IsNullOrEmpty(einsatz.Ergebnis) ? "Kein Ergebnis angegeben" : ErgebnisText.Anzeige(einsatz.Ergebnis))
                        .FontSize(14)
                        .Bold()
                        .FontColor(textColor);

                    if (!string.IsNullOrWhiteSpace(einsatz.Bemerkungen))
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
                            col.Item().Text("Keine Einträge").Italic().FontSize(9);
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
                            col.Item().Text("Keine Einträge").Italic().FontSize(9);
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
    }
}

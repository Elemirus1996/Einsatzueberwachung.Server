using ClosedXML.Excel;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class ExcelExportService
    {
        private Task ExportPersonalSheet(XLWorkbook workbook, List<PersonalEntry> personalList)
        {
            var ws = workbook.Worksheets.Add("Personal");

            ws.Cell(1, 1).Value = "Vorname";
            ws.Cell(1, 2).Value = "Nachname";
            ws.Cell(1, 3).Value = "Divera Benutzer-ID";
            ws.Cell(1, 4).Value = "Qualifikationen";
            ws.Cell(1, 5).Value = "Notizen";
            ws.Cell(1, 6).Value = "Aktiv";

            var headerRange = ws.Range(1, 1, 1, 6);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
            headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

            int row = 2;
            foreach (var person in personalList.OrderBy(p => p.Nachname).ThenBy(p => p.Vorname))
            {
                ws.Cell(row, 1).Value = person.Vorname;
                ws.Cell(row, 2).Value = person.Nachname;
                ws.Cell(row, 3).Value = person.DiveraUserId?.ToString() ?? string.Empty;
                ws.Cell(row, 4).Value = GetSkillsString(person.Skills);
                ws.Cell(row, 5).Value = person.Notizen;
                ws.Cell(row, 6).Value = person.IsActive ? "Ja" : "Nein";
                row++;
            }

            ws.Columns().AdjustToContents();
            return Task.CompletedTask;
        }

        private Task ExportHundeSheet(XLWorkbook workbook, List<DogEntry> dogList, List<PersonalEntry> personalList)
        {
            var ws = workbook.Worksheets.Add("Hunde");

            ws.Cell(1, 1).Value = "Name";
            ws.Cell(1, 2).Value = "Rasse";
            ws.Cell(1, 3).Value = "Alter";
            ws.Cell(1, 4).Value = "Spezialisierungen";
            ws.Cell(1, 5).Value = "Hundeführer";
            ws.Cell(1, 6).Value = "Notizen";
            ws.Cell(1, 7).Value = "Aktiv";

            var headerRange = ws.Range(1, 1, 1, 7);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGreen;
            headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

            int row = 2;
            foreach (var dog in dogList.OrderBy(d => d.Name))
            {
                var hundefuehrerNames = dog.HundefuehrerIds
                    .Select(id => personalList.FirstOrDefault(p => p.Id == id)?.FullName)
                    .Where(n => n != null);

                ws.Cell(row, 1).Value = dog.Name;
                ws.Cell(row, 2).Value = dog.Rasse;
                ws.Cell(row, 3).Value = dog.Alter;
                ws.Cell(row, 4).Value = GetSpecializationsString(dog.Specializations);
                ws.Cell(row, 5).Value = string.Join("; ", hundefuehrerNames);
                ws.Cell(row, 6).Value = dog.Notizen;
                ws.Cell(row, 7).Value = dog.IsActive ? "Ja" : "Nein";
                row++;
            }

            ws.Columns().AdjustToContents();
            return Task.CompletedTask;
        }

        private Task ExportDrohnenSheet(XLWorkbook workbook, List<DroneEntry> droneList, List<PersonalEntry> personalList)
        {
            var ws = workbook.Worksheets.Add("Drohnen");

            ws.Cell(1, 1).Value = "Name";
            ws.Cell(1, 2).Value = "Hersteller";
            ws.Cell(1, 3).Value = "Modell";
            ws.Cell(1, 4).Value = "Seriennummer";
            ws.Cell(1, 5).Value = "Drohnenpilot";
            ws.Cell(1, 6).Value = "Notizen";
            ws.Cell(1, 7).Value = "Aktiv";

            var headerRange = ws.Range(1, 1, 1, 7);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightCoral;
            headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

            int row = 2;
            foreach (var drone in droneList.OrderBy(d => d.Name))
            {
                var pilot = personalList.FirstOrDefault(p => p.Id == drone.DrohnenpilotId);

                ws.Cell(row, 1).Value = drone.Name;
                ws.Cell(row, 2).Value = drone.Hersteller;
                ws.Cell(row, 3).Value = drone.Modell;
                ws.Cell(row, 4).Value = drone.Seriennummer;
                ws.Cell(row, 5).Value = pilot?.FullName ?? "";
                ws.Cell(row, 6).Value = drone.Notizen;
                ws.Cell(row, 7).Value = drone.IsActive ? "Ja" : "Nein";
                row++;
            }

            ws.Columns().AdjustToContents();
            return Task.CompletedTask;
        }

        public Task<byte[]> ExportEinsatzAsync(EinsatzData einsatz, List<Team> teams, List<GlobalNotesEntry> notes)
        {
            using var workbook = new XLWorkbook();

            var wsInfo = workbook.Worksheets.Add("Einsatz");
            wsInfo.Cell(1, 1).Value = "Einsatzübersicht";
            wsInfo.Cell(1, 1).Style.Font.Bold = true;
            wsInfo.Cell(1, 1).Style.Font.FontSize = 14;

            var infoRows = new[]
            {
                ("Einsatznummer", einsatz.EinsatzNummer),
                ("Typ", einsatz.IstEinsatz ? "Einsatz" : "Übung"),
                ("Szenario", einsatz.Szenario.GetDisplayName()),
                ("Einsatzort", einsatz.Einsatzort),
                ("Alarmiert", einsatz.Alarmiert),
                ("Einsatzleiter", einsatz.Einsatzleiter),
                ("Führungsassistent", einsatz.Fuehrungsassistent),
                ("Exportiert am", Ortszeit.Jetzt.ToString("dd.MM.yyyy HH:mm"))
            };

            int r = 3;
            foreach (var (label, value) in infoRows)
            {
                wsInfo.Cell(r, 1).Value = label;
                wsInfo.Cell(r, 1).Style.Font.Bold = true;
                wsInfo.Cell(r, 2).Value = value ?? "";
                r++;
            }
            wsInfo.Columns().AdjustToContents();

            // Vermisste Personen
            if (einsatz.Vermisste is { Count: > 0 })
            {
                var wsVermisst = workbook.Worksheets.Add("Vermisste");
                string[] vHeaders =
                {
                    "Nr", "Vorname", "Nachname", "Alter", "Geburtsdatum",
                    "Kleidung", "Besonderheiten",
                    "Zuletzt gesehen (Ort)", "Uhrzeit", "Gesehen von",
                    "Orientierung", "Mobilität", "Suizidrisiko", "Bewaffnet",
                    "Vorerkrankungen", "Medikamente",
                    "Polizei (Name)", "Dienstnr.", "Telefon",
                    "BOS-Einheit", "Zugführer", "Funkrufname"
                };
                for (int i = 0; i < vHeaders.Length; i++)
                    wsVermisst.Cell(1, i + 1).Value = vHeaders[i];
                var vHeaderRange = wsVermisst.Range(1, 1, 1, vHeaders.Length);
                vHeaderRange.Style.Font.Bold = true;
                vHeaderRange.Style.Fill.BackgroundColor = XLColor.LightSalmon;

                int vr = 2;
                int vIdx = 1;
                foreach (var v in einsatz.Vermisste)
                {
                    wsVermisst.Cell(vr, 1).Value = vIdx++;
                    wsVermisst.Cell(vr, 2).Value = v.Vorname;
                    wsVermisst.Cell(vr, 3).Value = v.Nachname;
                    wsVermisst.Cell(vr, 4).Value = v.Alter;
                    wsVermisst.Cell(vr, 5).Value = v.Geburtsdatum;
                    wsVermisst.Cell(vr, 6).Value = v.Kleidung;
                    wsVermisst.Cell(vr, 7).Value = v.Besonderheiten;
                    wsVermisst.Cell(vr, 8).Value = v.ZuletztGesehenOrt;
                    wsVermisst.Cell(vr, 9).Value = v.ZuletztGesehenZeit;
                    wsVermisst.Cell(vr, 10).Value = v.ZuletztGesehenVon;
                    wsVermisst.Cell(vr, 11).Value = v.Orientierung.ToString();
                    wsVermisst.Cell(vr, 12).Value = v.Mobilitaet.ToString();
                    wsVermisst.Cell(vr, 13).Value = v.Suizidrisiko.ToString();
                    wsVermisst.Cell(vr, 14).Value = v.Bewaffnet.ToString();
                    wsVermisst.Cell(vr, 15).Value = v.Vorerkrankungen;
                    wsVermisst.Cell(vr, 16).Value = v.Medikamente;
                    wsVermisst.Cell(vr, 17).Value = v.PolizeiKontaktName;
                    wsVermisst.Cell(vr, 18).Value = v.PolizeiDienstnummer;
                    wsVermisst.Cell(vr, 19).Value = v.PolizeiTelefon;
                    wsVermisst.Cell(vr, 20).Value = v.BosEinheit;
                    wsVermisst.Cell(vr, 21).Value = v.BosZugfuehrer;
                    wsVermisst.Cell(vr, 22).Value = v.BosFunkrufname;
                    vr++;
                }
                wsVermisst.Columns().AdjustToContents();

                // Checklisten als separater Sheet, falls mindestens eine Person eine hat
                if (einsatz.Vermisste.Any(v => v.Checkliste is { Items.Count: > 0 }))
                {
                    var wsCheck = workbook.Worksheets.Add("Checklisten");
                    string[] cHeaders = { "Person", "Szenario", "Item", "Pflicht", "Antwort" };
                    for (int i = 0; i < cHeaders.Length; i++)
                        wsCheck.Cell(1, i + 1).Value = cHeaders[i];
                    var cHeaderRange = wsCheck.Range(1, 1, 1, cHeaders.Length);
                    cHeaderRange.Style.Font.Bold = true;
                    cHeaderRange.Style.Fill.BackgroundColor = XLColor.LightCyan;

                    int cr = 2;
                    foreach (var v in einsatz.Vermisste)
                    {
                        if (v.Checkliste is null) continue;
                        var name = string.IsNullOrWhiteSpace(v.VollerName) || v.VollerName == "Unbekannt"
                            ? "(ohne Name)" : v.VollerName;
                        foreach (var item in v.Checkliste.Items)
                        {
                            v.Checkliste.Values.TryGetValue(item.Id.ToString(), out var raw);
                            var answer = item.Type == ChecklistItemType.Bool
                                ? (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ? "ja" : "—")
                                : (string.IsNullOrWhiteSpace(raw) ? "—" : raw!);

                            wsCheck.Cell(cr, 1).Value = name;
                            wsCheck.Cell(cr, 2).Value = v.Checkliste.Szenario.GetDisplayName();
                            wsCheck.Cell(cr, 3).Value = item.Label;
                            wsCheck.Cell(cr, 4).Value = item.Required ? "ja" : "";
                            wsCheck.Cell(cr, 5).Value = answer;
                            cr++;
                        }
                    }
                    wsCheck.Columns().AdjustToContents();
                }
            }

            var wsTeams = workbook.Worksheets.Add("Teams");
            string[] teamHeaders = ["Team", "Typ", "Hund", "Hundeführer", "Helfer", "Suchgebiet", "Laufzeit", "Status", "Notizen"];
            for (int i = 0; i < teamHeaders.Length; i++)
                wsTeams.Cell(1, i + 1).Value = teamHeaders[i];
            var teamHeaderRange = wsTeams.Range(1, 1, 1, teamHeaders.Length);
            teamHeaderRange.Style.Font.Bold = true;
            teamHeaderRange.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

            int tr = 2;
            foreach (var team in teams.OrderBy(t => t.TeamName))
            {
                string typ = team.IsDroneTeam ? "Drohnenteam" : team.IsSupportTeam ? "Unterstützung" : "Hundeteam";
                string status = team.IsRunning ? "Läuft" : team.IsPausing ? "Pause" : "Gestoppt";
                wsTeams.Cell(tr, 1).Value = team.TeamName;
                wsTeams.Cell(tr, 2).Value = typ;
                wsTeams.Cell(tr, 3).Value = team.DogName ?? "";
                wsTeams.Cell(tr, 4).Value = team.HundefuehrerName ?? "";
                wsTeams.Cell(tr, 5).Value = team.HelferNamesJoined;
                wsTeams.Cell(tr, 6).Value = team.SearchAreaName ?? "";
                wsTeams.Cell(tr, 7).Value = team.ElapsedTime.ToString(@"hh\:mm\:ss");
                wsTeams.Cell(tr, 8).Value = status;
                wsTeams.Cell(tr, 9).Value = team.Notes ?? "";
                tr++;
            }
            wsTeams.Columns().AdjustToContents();

            var wsNotes = workbook.Worksheets.Add("Notizen");
            string[] noteHeaders = ["Zeit", "Typ", "Quelle", "Text"];
            for (int i = 0; i < noteHeaders.Length; i++)
                wsNotes.Cell(1, i + 1).Value = noteHeaders[i];
            var noteHeaderRange = wsNotes.Range(1, 1, 1, noteHeaders.Length);
            noteHeaderRange.Style.Font.Bold = true;
            noteHeaderRange.Style.Fill.BackgroundColor = XLColor.LightYellow;

            int nr = 2;
            foreach (var note in notes.OrderBy(n => n.Timestamp))
            {
                wsNotes.Cell(nr, 1).Value = note.Timestamp.ToString("dd.MM.yyyy HH:mm:ss");
                wsNotes.Cell(nr, 2).Value = note.SourceType ?? "";
                wsNotes.Cell(nr, 3).Value = note.SourceTeamName ?? "";
                wsNotes.Cell(nr, 4).Value = note.Text ?? "";
                nr++;
            }
            wsNotes.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return Task.FromResult(stream.ToArray());
        }
    }
}

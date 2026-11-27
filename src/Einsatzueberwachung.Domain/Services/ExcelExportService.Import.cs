using ClosedXML.Excel;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class ExcelExportService
    {
        public async Task<ImportResult> ImportStammdatenAsync(byte[] excelData)
        {
            var result = new ImportResult { Success = true };

            try
            {
                using var stream = new MemoryStream(excelData);
                using var workbook = new XLWorkbook(stream);

                var personalList = await _masterDataService.GetPersonalListAsync();

                if (workbook.Worksheets.TryGetWorksheet("Personal", out var personalSheet))
                {
                    await ImportPersonalSheet(personalSheet, result);
                    personalList = await _masterDataService.GetPersonalListAsync();
                }

                if (workbook.Worksheets.TryGetWorksheet("Hunde", out var hundeSheet))
                    await ImportHundeSheet(hundeSheet, personalList, result);

                if (workbook.Worksheets.TryGetWorksheet("Drohnen", out var drohnenSheet))
                    await ImportDrohnenSheet(drohnenSheet, personalList, result);

                result.Message = $"Import erfolgreich: {result.TotalImported} Einträge importiert" +
                                 (result.TotalSkipped > 0 ? $", {result.TotalSkipped} übersprungen" : "");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Fehler beim Import: {ex.Message}";
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        private async Task ImportPersonalSheet(IXLWorksheet ws, ImportResult result)
        {
            var existingPersonal = await _masterDataService.GetPersonalListAsync();
            var rows = ws.RowsUsed().Skip(1);

            var colVorname = GetColumnIndexByHeader(ws, "Vorname") ?? 1;
            var colNachname = GetColumnIndexByHeader(ws, "Nachname") ?? 2;
            var colDiveraId = GetColumnIndexByHeader(ws, "Divera Benutzer-ID", "Divera ID", "DiveraUserId");
            var colSkills = GetColumnIndexByHeader(ws, "Qualifikationen") ?? (colDiveraId.HasValue ? 4 : 3);
            var colNotizen = GetColumnIndexByHeader(ws, "Notizen") ?? (colDiveraId.HasValue ? 5 : 4);
            var colAktiv = GetColumnIndexByHeader(ws, "Aktiv") ?? (colDiveraId.HasValue ? 6 : 5);
            var colOrganisation = GetColumnIndexByHeader(ws, "Organisation", "Organisation/Einheit");

            foreach (var row in rows)
            {
                try
                {
                    var vorname = row.Cell(colVorname).GetString().Trim();
                    var nachname = row.Cell(colNachname).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(vorname) && string.IsNullOrWhiteSpace(nachname))
                        continue;

                    var existing = existingPersonal.FirstOrDefault(p =>
                        p.Vorname.Equals(vorname, StringComparison.OrdinalIgnoreCase) &&
                        p.Nachname.Equals(nachname, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        result.PersonalSkipped++;
                        result.Warnings.Add($"Personal '{vorname} {nachname}' existiert bereits und wurde übersprungen");
                        continue;
                    }

                    var person = new PersonalEntry
                    {
                        Vorname = vorname,
                        Nachname = nachname,
                        DiveraUserId = colDiveraId.HasValue ? ParseNullableInt(row.Cell(colDiveraId.Value).GetString()) : null,
                        Skills = ParseSkills(row.Cell(colSkills).GetString()),
                        Notizen = row.Cell(colNotizen).GetString().Trim(),
                        Organisation = colOrganisation.HasValue ? row.Cell(colOrganisation.Value).GetString().Trim() : string.Empty,
                        IsActive = ParseBool(row.Cell(colAktiv).GetString())
                    };

                    await _masterDataService.AddPersonalAsync(person);
                    result.PersonalImported++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Zeile {row.RowNumber()}: {ex.Message}");
                }
            }
        }

        private async Task ImportHundeSheet(IXLWorksheet ws, List<PersonalEntry> personalList, ImportResult result)
        {
            var existingDogs = await _masterDataService.GetDogListAsync();
            var rows = ws.RowsUsed().Skip(1);
            var colOrganisation = GetColumnIndexByHeader(ws, "Organisation", "Organisation/Einheit");

            foreach (var row in rows)
            {
                try
                {
                    var name = row.Cell(1).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var existing = existingDogs.FirstOrDefault(d =>
                        d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        result.HundeSkipped++;
                        result.Warnings.Add($"Hund '{name}' existiert bereits und wurde übersprungen");
                        continue;
                    }

                    var hundefuehrerCell = row.Cell(5).GetString().Trim();
                    var hundefuehrerNames = hundefuehrerCell
                        .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(n => n.Trim())
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList();
                    var hundefuehrerIds = new List<string>();
                    var missingNames = new List<string>();
                    foreach (var hfName in hundefuehrerNames)
                    {
                        var hf = FindPersonByName(personalList, hfName);
                        if (hf != null)
                            hundefuehrerIds.Add(hf.Id);
                        else
                            missingNames.Add(hfName);
                    }

                    var dog = new DogEntry
                    {
                        Name = name,
                        Rasse = row.Cell(2).GetString().Trim(),
                        Alter = ParseInt(row.Cell(3).GetString()),
                        Specializations = ParseSpecializations(row.Cell(4).GetString()),
                        HundefuehrerIds = hundefuehrerIds,
                        Notizen = row.Cell(6).GetString().Trim(),
                        Organisation = colOrganisation.HasValue ? row.Cell(colOrganisation.Value).GetString().Trim() : string.Empty,
                        IsActive = ParseBool(row.Cell(7).GetString())
                    };

                    await _masterDataService.AddDogAsync(dog);
                    result.HundeImported++;

                    foreach (var missing in missingNames)
                        result.Warnings.Add($"Hundeführer '{missing}' für Hund '{name}' nicht gefunden");
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Hunde Zeile {row.RowNumber()}: {ex.Message}");
                }
            }
        }

        private async Task ImportDrohnenSheet(IXLWorksheet ws, List<PersonalEntry> personalList, ImportResult result)
        {
            var existingDrones = await _masterDataService.GetDroneListAsync();
            var rows = ws.RowsUsed().Skip(1);

            foreach (var row in rows)
            {
                try
                {
                    var name = row.Cell(1).GetString().Trim();
                    var seriennummer = row.Cell(4).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(seriennummer))
                        continue;

                    var existing = existingDrones.FirstOrDefault(d =>
                        (!string.IsNullOrWhiteSpace(name) && d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(seriennummer) && d.Seriennummer.Equals(seriennummer, StringComparison.OrdinalIgnoreCase)));

                    if (existing != null)
                    {
                        result.DrohnenSkipped++;
                        result.Warnings.Add($"Drohne '{name}' existiert bereits und wurde übersprungen");
                        continue;
                    }

                    var pilotName = row.Cell(5).GetString().Trim();
                    var pilot = FindPersonByName(personalList, pilotName);

                    var drone = new DroneEntry
                    {
                        Name = name,
                        Hersteller = row.Cell(2).GetString().Trim(),
                        Modell = row.Cell(3).GetString().Trim(),
                        Seriennummer = seriennummer,
                        DrohnenpilotId = pilot?.Id ?? "",
                        Notizen = row.Cell(6).GetString().Trim(),
                        IsActive = ParseBool(row.Cell(7).GetString())
                    };

                    await _masterDataService.AddDroneAsync(drone);
                    result.DrohnenImported++;

                    if (!string.IsNullOrWhiteSpace(pilotName) && pilot == null)
                        result.Warnings.Add($"Drohnenpilot '{pilotName}' für Drohne '{name}' nicht gefunden");
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Drohnen Zeile {row.RowNumber()}: {ex.Message}");
                }
            }
        }

        public Task<byte[]> CreateImportTemplateAsync()
        {
            using var workbook = new XLWorkbook();

            var wsPersonal = workbook.Worksheets.Add("Personal");
            wsPersonal.Cell(1, 1).Value = "Vorname";
            wsPersonal.Cell(1, 2).Value = "Nachname";
            wsPersonal.Cell(1, 3).Value = "Divera Benutzer-ID";
            wsPersonal.Cell(1, 4).Value = "Qualifikationen";
            wsPersonal.Cell(1, 5).Value = "Notizen";
            wsPersonal.Cell(1, 6).Value = "Aktiv";
            var personalHeader = wsPersonal.Range(1, 1, 1, 6);
            personalHeader.Style.Font.Bold = true;
            personalHeader.Style.Fill.BackgroundColor = XLColor.LightBlue;
            wsPersonal.Cell(2, 1).Value = "Max";
            wsPersonal.Cell(2, 2).Value = "Mustermann";
            wsPersonal.Cell(2, 3).Value = "681743";
            wsPersonal.Cell(2, 4).Value = "Hundeführer, Helfer";
            wsPersonal.Cell(2, 5).Value = "Beispiel-Notiz";
            wsPersonal.Cell(2, 6).Value = "Ja";
            wsPersonal.Cell(4, 1).Value = "Mögliche Qualifikationen:";
            wsPersonal.Cell(4, 1).Style.Font.Bold = true;
            wsPersonal.Cell(5, 1).Value = "Hundeführer, Helfer, Führungsassistent, Gruppenführer, Zugführer, Verbandsführer, Drohnenpilot, Einsatzleiter";
            wsPersonal.Cell(7, 1).Value = "Hinweis Divera Benutzer-ID:";
            wsPersonal.Cell(7, 1).Style.Font.Bold = true;
            wsPersonal.Cell(8, 1).Value = "Numerische Divera-ID (z.B. 681743). Optional, aber empfohlen für Namensauflösung bei Rückmeldungen.";
            wsPersonal.Columns().AdjustToContents();

            var wsHunde = workbook.Worksheets.Add("Hunde");
            wsHunde.Cell(1, 1).Value = "Name";
            wsHunde.Cell(1, 2).Value = "Rasse";
            wsHunde.Cell(1, 3).Value = "Alter";
            wsHunde.Cell(1, 4).Value = "Spezialisierungen";
            wsHunde.Cell(1, 5).Value = "Hundeführer";
            wsHunde.Cell(1, 6).Value = "Notizen";
            wsHunde.Cell(1, 7).Value = "Aktiv";
            var hundeHeader = wsHunde.Range(1, 1, 1, 7);
            hundeHeader.Style.Font.Bold = true;
            hundeHeader.Style.Fill.BackgroundColor = XLColor.LightGreen;
            wsHunde.Cell(2, 1).Value = "Rex";
            wsHunde.Cell(2, 2).Value = "Schäferhund";
            wsHunde.Cell(2, 3).Value = "5";
            wsHunde.Cell(2, 4).Value = "Flächensuche, Trümmersuche";
            wsHunde.Cell(2, 5).Value = "Max Mustermann";
            wsHunde.Cell(2, 6).Value = "Beispiel-Notiz";
            wsHunde.Cell(2, 7).Value = "Ja";
            wsHunde.Cell(4, 1).Value = "Mögliche Spezialisierungen:";
            wsHunde.Cell(4, 1).Style.Font.Bold = true;
            wsHunde.Cell(5, 1).Value = "Flächensuche, Trümmersuche, Wasserortung, Mantrailing, Lawinensuche";
            wsHunde.Columns().AdjustToContents();

            var wsDrohnen = workbook.Worksheets.Add("Drohnen");
            wsDrohnen.Cell(1, 1).Value = "Name";
            wsDrohnen.Cell(1, 2).Value = "Hersteller";
            wsDrohnen.Cell(1, 3).Value = "Modell";
            wsDrohnen.Cell(1, 4).Value = "Seriennummer";
            wsDrohnen.Cell(1, 5).Value = "Drohnenpilot";
            wsDrohnen.Cell(1, 6).Value = "Notizen";
            wsDrohnen.Cell(1, 7).Value = "Aktiv";
            var drohnenHeader = wsDrohnen.Range(1, 1, 1, 7);
            drohnenHeader.Style.Font.Bold = true;
            drohnenHeader.Style.Fill.BackgroundColor = XLColor.LightCoral;
            wsDrohnen.Cell(2, 1).Value = "Drohne 1";
            wsDrohnen.Cell(2, 2).Value = "DJI";
            wsDrohnen.Cell(2, 3).Value = "Mavic 3";
            wsDrohnen.Cell(2, 4).Value = "SN123456789";
            wsDrohnen.Cell(2, 5).Value = "Max Mustermann";
            wsDrohnen.Cell(2, 6).Value = "Beispiel-Notiz";
            wsDrohnen.Cell(2, 7).Value = "Ja";
            wsDrohnen.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return Task.FromResult(stream.ToArray());
        }
    }
}

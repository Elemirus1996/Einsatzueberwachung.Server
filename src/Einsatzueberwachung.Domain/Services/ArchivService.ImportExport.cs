using System.Text.Json;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Merge;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class ArchivService
    {
        public async Task<ArchivedEinsatz> ImportPacketAsNewEinsatzAsync(
            EinsatzExportPacket packet,
            string einsatzort = "",
            string ergebnis = "",
            string bemerkungen = "")
        {
            await EnsureLoadedAsync();

            var archived = new ArchivedEinsatz
            {
                ArchivedAt = Now,
                EinsatzNummer = packet.EinsatzNummer,
                Einsatzort = !string.IsNullOrWhiteSpace(einsatzort) ? einsatzort :
                             !string.IsNullOrWhiteSpace(packet.Label) ? packet.Label :
                             packet.EinsatzNummer,
                StaffelName = packet.Label,
                IstEinsatz = true,
                EinsatzDatum = Ortszeit.AusUtc(packet.ExportedAt).Date,
                AlarmierungsZeit = Ortszeit.AusUtc(packet.ExportedAt),
                EinsatzEnde = Now,
                Ergebnis = ergebnis,
                Bemerkungen = bemerkungen,
                AnzahlTeams = packet.Teams.Count,
                GlobalNotesEntries = packet.Notes.ToList(),
                SearchAreas = packet.SearchAreas.ToList(),
                TrackSnapshots = packet.TrackSnapshots.ToList(),
            };

            foreach (var team in packet.Teams)
                archived.Teams.Add(ArchivedTeam.FromTeam(team));

            archived.PersonalNamen = packet.Personal
                .Select(p => p.FullName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            archived.HundeNamen = packet.Dogs
                .Select(d => d.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            archived.DrohnenNamen = packet.Drones
                .Select(d => d.DisplayName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            archived.AnzahlPersonal = archived.PersonalNamen.Count;
            archived.AnzahlHunde = archived.HundeNamen.Count;
            archived.AnzahlDrohnen = archived.DrohnenNamen.Count;
            archived.AnzahlRessourcen = archived.AnzahlPersonal + archived.AnzahlHunde + archived.AnzahlDrohnen;

            _archiv.Insert(0, archived);
            await SaveAsync();

            return archived;
        }

        public async Task<byte[]> ExportAllAsJsonAsync()
        {
            await EnsureLoadedAsync();

            var exportData = new
            {
                ExportDatum = Now,
                Version = "3.12.0",
                AnzahlEinsaetze = _archiv.Count,
                Einsaetze = _archiv
            };

            var json = JsonSerializer.Serialize(exportData, JsonOptions);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        public async Task<int> ImportFromJsonAsync(byte[] jsonData)
        {
            await EnsureLoadedAsync();

            var json = System.Text.Encoding.UTF8.GetString(jsonData);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                List<ArchivedEinsatz>? importedList = null;

                if (root.TryGetProperty("einsaetze", out var einsaetzeElement))
                {
                    importedList = JsonSerializer.Deserialize<List<ArchivedEinsatz>>(einsaetzeElement.GetRawText(), JsonOptions);
                }
                else
                {
                    importedList = JsonSerializer.Deserialize<List<ArchivedEinsatz>>(json, JsonOptions);
                }

                if (importedList == null || importedList.Count == 0)
                    return 0;

                int imported = 0;
                foreach (var einsatz in importedList)
                {
                    if (!_archiv.Any(e => e.Id == einsatz.Id))
                    {
                        _archiv.Add(einsatz);
                        imported++;
                    }
                }

                if (imported > 0)
                {
                    await SaveAsync();
                }

                return imported;
            }
            catch
            {
                return 0;
            }
        }
    }
}

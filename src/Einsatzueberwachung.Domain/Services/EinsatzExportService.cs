// EinsatzExportService — Erstellt ein portables Export-Paket aus dem laufenden Einsatz

using System.Text;
using System.Text.Json;
using Einsatzueberwachung.Domain.Interfaces;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzExportService : IEinsatzExportService
    {
        private readonly IEinsatzService _einsatzService;
        private readonly IMasterDataService _masterDataService;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null
        };

        public EinsatzExportService(
            IEinsatzService einsatzService,
            IMasterDataService masterDataService)
        {
            _einsatzService = einsatzService;
            _masterDataService = masterDataService;
        }

        public async Task<EinsatzExportPacket> BuildExportPacketAsync(
            IEnumerable<string> selectedTeamIds,
            string label,
            EinsatzExportOptions? options = null)
        {
            options ??= new EinsatzExportOptions();

            var teamIdSet = new HashSet<string>(selectedTeamIds, StringComparer.OrdinalIgnoreCase);
            var selectedTeams = _einsatzService.Teams
                .Where(t => teamIdSet.Contains(t.TeamId))
                .ToList();

            var einsatz = _einsatzService.CurrentEinsatz;

            var packet = new EinsatzExportPacket
            {
                Label = label.Trim(),
                ExportedAt = DateTime.UtcNow,
                EinsatzNummer = einsatz.EinsatzNummer
            };

            packet.Teams.AddRange(selectedTeams);

            await CollectStammdatenAsync(packet, selectedTeams);

            CollectNotes(packet, teamIdSet, einsatz.GlobalNotesEntries ?? new(), options);

            CollectSearchAreas(packet, teamIdSet, einsatz.SearchAreas ?? new(), options);

            if (options.IncludeMapMarkers)
            {
                var markers = await _einsatzService.GetMapMarkersAsync();
                packet.MapMarkers.AddRange(markers);
            }

            if (options.IncludeTrackSnapshots)
            {
                CollectTrackSnapshots(packet, selectedTeams, teamIdSet, einsatz);
            }

            // Vermisste werden immer mit-exportiert — sie gehören zur Einsatzlage und sind nicht teamspezifisch.
            if (einsatz.Vermisste is { Count: > 0 })
            {
                packet.Vermisste.AddRange(einsatz.Vermisste);
            }

            return packet;
        }

        public Task<EinsatzExportPacket> BuildExportPacketFromArchiveAsync(
            ArchivedEinsatz archived,
            IEnumerable<string> selectedTeamIds,
            EinsatzExportOptions? options = null)
        {
            options ??= new EinsatzExportOptions();

            var teamIdSet = new HashSet<string>(selectedTeamIds, StringComparer.OrdinalIgnoreCase);
            var selectedTeams = archived.Teams
                .Where(t => teamIdSet.Contains(t.TeamId))
                .ToList();

            var packet = new EinsatzExportPacket
            {
                ExportedAt = DateTime.UtcNow,
                EinsatzNummer = archived.EinsatzNummer
            };

            foreach (var archivedTeam in selectedTeams)
                packet.Teams.Add(ConvertArchivedTeamToTeam(archivedTeam));

            CollectNotes(packet, teamIdSet, archived.GlobalNotesEntries ?? new(), options);

            CollectSearchAreas(packet, teamIdSet, archived.SearchAreas ?? new(), options);

            if (options.IncludeTrackSnapshots)
            {
                var collectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var archivedTeam in selectedTeams)
                {
                    foreach (var snap in archivedTeam.TrackSnapshots ?? new())
                    {
                        if (collectedIds.Add(snap.Id))
                            packet.TrackSnapshots.Add(snap);
                    }
                }

                foreach (var snap in archived.TrackSnapshots ?? new())
                {
                    if (collectedIds.Contains(snap.Id)) continue;
                    if (!string.IsNullOrEmpty(snap.TeamId) && teamIdSet.Contains(snap.TeamId))
                    {
                        if (collectedIds.Add(snap.Id))
                            packet.TrackSnapshots.Add(snap);
                    }
                }
            }

            if (archived.Vermisste is { Count: > 0 })
            {
                packet.Vermisste.AddRange(archived.Vermisste);
            }

            return Task.FromResult(packet);
        }

        private static Team ConvertArchivedTeamToTeam(ArchivedTeam archivedTeam)
        {
            var isDrone = !string.IsNullOrEmpty(archivedTeam.DroneName);
            var team = new Team
            {
                TeamId = archivedTeam.TeamId,
                TeamName = archivedTeam.TeamName,
                HundefuehrerName = archivedTeam.MemberNames.ElementAtOrDefault(0) ?? string.Empty,
                DogName = archivedTeam.DogName ?? string.Empty,
                IsDroneTeam = isDrone,
                DroneType = archivedTeam.DroneName ?? string.Empty,
                TrackSnapshots = archivedTeam.TrackSnapshots?.ToList() ?? new()
            };

            for (int i = 1; i < archivedTeam.MemberNames.Count && team.HelferNames.Count < 3; i++)
            {
                var name = archivedTeam.MemberNames[i];
                if (string.IsNullOrWhiteSpace(name)) continue;
                team.HelferIds.Add(string.Empty);
                team.HelferNames.Add(name);
            }

            return team;
        }

        public byte[] Serialize(EinsatzExportPacket packet)
        {
            var json = JsonSerializer.Serialize(packet, JsonOptions);
            return Encoding.UTF8.GetBytes(json);
        }

        public string GetFileName(EinsatzExportPacket packet)
        {
            var safeNr = MakeSafeFileName(packet.EinsatzNummer);
            var timestamp = Ortszeit.AusUtc(packet.ExportedAt).ToString("yyyyMMdd_HHmm");

            if (!string.IsNullOrWhiteSpace(packet.Label))
            {
                var safeName = MakeSafeFileName(packet.Label);
                return $"{safeName}_{safeNr}_{timestamp}.einsatz-export.json";
            }

            return $"export_{safeNr}_{timestamp}.einsatz-export.json";
        }

        private const string FallbackLabelName = "unbekannt";
        private const string FallbackExportName = "export";

        private static string MakeSafeFileName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return FallbackLabelName;

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(input.Length);
            foreach (var c in input)
                sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);

            var result = sb.ToString().Trim('_');
            return result.Length == 0 ? FallbackExportName :
                   result.Length > 50 ? result[..50] : result;
        }
    }
}

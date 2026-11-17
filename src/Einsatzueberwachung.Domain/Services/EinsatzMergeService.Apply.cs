using System.Text.Json;
using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzMergeService
    {
        public async Task<MergeHistoryEntry> ApplyMergeAsync(EinsatzMergeSession session)
        {
            if (!session.AllMasterDataResolved)
                throw new InvalidOperationException("Nicht alle Stammdaten-Einträge wurden zugeordnet.");

            var historyEntry = new MergeHistoryEntry
            {
                MergedAt = Now,
                Label = session.Packet.Label
            };

            var finalRemapping = new Dictionary<string, string>(session.IdRemapping);

            foreach (var item in session.PersonalItems.Where(i => i.Decision == MergeDecision.CreateNew))
            {
                var entry = (PersonalEntry)item.ImportedEntry;
                var newEntry = ClonePersonal(entry);
                await _masterDataService.AddPersonalAsync(newEntry);
                finalRemapping[item.ImportedId] = newEntry.Id;
                historyEntry.CreatedPersonalIds.Add(newEntry.Id);
                historyEntry.PersonalCreated++;
            }

            foreach (var item in session.DogItems.Where(i => i.Decision == MergeDecision.CreateNew))
            {
                var entry = (DogEntry)item.ImportedEntry;
                var newEntry = CloneDog(entry);
                await _masterDataService.AddDogAsync(newEntry);
                finalRemapping[item.ImportedId] = newEntry.Id;
                historyEntry.CreatedDogIds.Add(newEntry.Id);
                historyEntry.DogsCreated++;
            }

            foreach (var item in session.DroneItems.Where(i => i.Decision == MergeDecision.CreateNew))
            {
                var entry = (DroneEntry)item.ImportedEntry;
                var newEntry = CloneDrone(entry);
                await _masterDataService.AddDroneAsync(newEntry);
                finalRemapping[item.ImportedId] = newEntry.Id;
                historyEntry.CreatedDroneIds.Add(newEntry.Id);
                historyEntry.DronesCreated++;
            }

            await ApplyFieldOverwritesAsync(session.PersonalItems, finalRemapping);
            await ApplyDogFieldOverwritesAsync(session.DogItems, finalRemapping);
            await ApplyDroneFieldOverwritesAsync(session.DroneItems, finalRemapping);

            if (session.TargetArchivedEinsatzId == null)
            {
                await ApplyToActiveEinsatzAsync(session, finalRemapping, historyEntry);
            }
            else
            {
                await ApplyToArchivedEinsatzAsync(session, finalRemapping, historyEntry);
            }

            historyEntry.TeamsAdded = historyEntry.AddedTeamIds.Count;
            historyEntry.NotesAdded = historyEntry.AddedNoteIds.Count;
            historyEntry.SearchAreasAdded = historyEntry.AddedSearchAreaIds.Count;
            historyEntry.MarkersAdded = historyEntry.AddedMapMarkerIds.Count;
            historyEntry.TracksAdded = historyEntry.AddedTrackSnapshotIds.Count;

            await PersistMergeHistoryEntryAsync(historyEntry, session.TargetArchivedEinsatzId);

            var label = string.IsNullOrWhiteSpace(session.Packet.Label)
                ? "Import"
                : $"Import '{session.Packet.Label}'";
            var summaryText = $"{label} zusammengeführt am " +
                              $"{historyEntry.MergedAt:dd.MM.yyyy HH:mm} — " +
                              $"{historyEntry.TeamsAdded} Teams, {historyEntry.NotesAdded} Notizen, " +
                              $"{historyEntry.SearchAreasAdded} Gebiete integriert.";

            if (session.TargetArchivedEinsatzId == null)
            {
                await _einsatzService.AddGlobalNoteAsync(summaryText, GlobalNotesEntryType.System);
            }

            session.AppliedMerge = historyEntry;
            session.CurrentStep = MergeWizardStep.Result;

            return historyEntry;
        }

        public async Task RevertMergeAsync(string mergeId, string? archivedEinsatzId = null)
        {
            var history = await GetMergeHistoryAsync(archivedEinsatzId);
            var entry = history.FirstOrDefault(e => e.MergeId == mergeId);
            if (entry == null || entry.IsReverted)
                return;

            if (archivedEinsatzId == null)
            {
                foreach (var id in entry.AddedTeamIds)
                    await _einsatzService.RemoveTeamAsync(id);

                foreach (var id in entry.AddedNoteIds)
                    await _einsatzService.RemoveGlobalNoteAsync(id);

                foreach (var id in entry.AddedSearchAreaIds)
                    await _einsatzService.DeleteSearchAreaAsync(id);

                foreach (var id in entry.AddedMapMarkerIds)
                    await _einsatzService.RemoveMapMarkerAsync(id);

                var einsatzData = _einsatzService.CurrentEinsatz;
                var trackSet = new HashSet<string>(entry.AddedTrackSnapshotIds);
                einsatzData.TrackSnapshots.RemoveAll(t => trackSet.Contains(t.Id));

                foreach (var idStr in entry.AddedVermisstenIds)
                {
                    if (Guid.TryParse(idStr, out var vid))
                        await _einsatzService.RemoveVermisstenAsync(vid);
                }

                foreach (var id in entry.CreatedPersonalIds)
                    await _masterDataService.DeletePersonalAsync(id);
                foreach (var id in entry.CreatedDogIds)
                    await _masterDataService.DeleteDogAsync(id);
                foreach (var id in entry.CreatedDroneIds)
                    await _masterDataService.DeleteDroneAsync(id);

                entry.IsReverted = true;
                entry.RevertedAt = Now;

                var revertLabel = string.IsNullOrWhiteSpace(entry.Label)
                    ? "Import"
                    : $"Import '{entry.Label}'";
                await _einsatzService.AddGlobalNoteAsync(
                    $"{revertLabel} vom {entry.FormattedMergedAt} rückgängig gemacht.",
                    GlobalNotesEntryType.System);
            }
            else
            {
                var archived = await _archivService.GetByIdAsync(archivedEinsatzId);
                if (archived == null) return;

                var teamSet = new HashSet<string>(entry.AddedTeamIds);
                var noteSet = new HashSet<string>(entry.AddedNoteIds);
                var areaSet = new HashSet<string>(entry.AddedSearchAreaIds);
                var markerSet = new HashSet<string>(entry.AddedMapMarkerIds);
                var trackSet = new HashSet<string>(entry.AddedTrackSnapshotIds);

                archived.Teams.RemoveAll(t => teamSet.Contains(t.TeamId));
                archived.GlobalNotesEntries.RemoveAll(n => noteSet.Contains(n.Id));
                archived.SearchAreas.RemoveAll(a => areaSet.Contains(a.Id));
                archived.TrackSnapshots.RemoveAll(t => trackSet.Contains(t.Id));

                if (archived.Vermisste is not null)
                {
                    var vermisstenSet = new HashSet<Guid>(
                        entry.AddedVermisstenIds
                            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                            .Where(g => g != Guid.Empty));
                    archived.Vermisste.RemoveAll(v => vermisstenSet.Contains(v.Id));
                }

                foreach (var id in entry.CreatedPersonalIds)
                    await _masterDataService.DeletePersonalAsync(id);
                foreach (var id in entry.CreatedDogIds)
                    await _masterDataService.DeleteDogAsync(id);
                foreach (var id in entry.CreatedDroneIds)
                    await _masterDataService.DeleteDroneAsync(id);

                entry.IsReverted = true;
                entry.RevertedAt = Now;

                await _archivService.UpdateArchivedEinsatzAsync(archived);
            }
        }

        private async Task ApplyToActiveEinsatzAsync(
            EinsatzMergeSession session,
            Dictionary<string, string> remapping,
            MergeHistoryEntry historyEntry)
        {
            var einsatz = _einsatzService.CurrentEinsatz;
            var localTrackIds = new HashSet<string>(einsatz.TrackSnapshots.Select(t => t.Id));
            var localMarkerIds = new HashSet<string>((await _einsatzService.GetMapMarkersAsync()).Select(m => m.Id));

            foreach (var teamItem in session.TeamItems.Where(t => t.ShouldImport))
            {
                var team = ApplyRemappingToTeam(teamItem.ImportedTeam, remapping);

                if (teamItem.HasLocalConflict)
                {
                    team.TeamId = Guid.NewGuid().ToString();
                }

                await _einsatzService.AddTeamAsync(team);
                historyEntry.AddedTeamIds.Add(team.TeamId);
            }

            foreach (var noteItem in session.NoteItems.Where(n => !n.IsAlreadyPresent && n.ShouldImport))
            {
                var note = noteItem.Note;
                await _einsatzService.ImportGlobalNoteAsync(note);
                historyEntry.AddedNoteIds.Add(note.Id);
            }

            foreach (var areaItem in session.SearchAreaItems.Where(
                a => a.ShouldImport && a.ConflictType != SearchAreaConflict.SameIdExists))
            {
                var area = areaItem.ImportedArea;

                if (!string.IsNullOrEmpty(area.AssignedTeamId) &&
                    remapping.TryGetValue(area.AssignedTeamId, out var mappedTeamId))
                {
                    area.AssignedTeamId = mappedTeamId;
                }

                if (areaItem.ConflictType == SearchAreaConflict.SameNameDifferentId)
                {
                    switch (areaItem.NameConflictResolution)
                    {
                        case SearchAreaNameConflictResolution.Rename:
                            area.Name = area.Name + SearchAreaRenameSuffix;
                            break;

                        case SearchAreaNameConflictResolution.ReplaceLocal:
                            await _einsatzService.DeleteSearchAreaAsync(areaItem.LocalConflictArea!.Id);
                            break;

                        case SearchAreaNameConflictResolution.KeepBoth:
                            area.Id = Guid.NewGuid().ToString();
                            break;
                    }
                }

                await _einsatzService.AddSearchAreaAsync(area);
                historyEntry.AddedSearchAreaIds.Add(area.Id);
            }

            foreach (var marker in session.Packet.MapMarkers.Where(m => !localMarkerIds.Contains(m.Id)))
            {
                await _einsatzService.AddMapMarkerAsync(marker);
                historyEntry.AddedMapMarkerIds.Add(marker.Id);
            }

            foreach (var track in session.Packet.TrackSnapshots.Where(t => !localTrackIds.Contains(t.Id)))
            {
                einsatz.TrackSnapshots.Add(track);
                historyEntry.AddedTrackSnapshotIds.Add(track.Id);
            }

            // Vermisste übernehmen — wenn Id schon existiert, überspringen (kein Diff-Merge).
            // Deep-Clone, damit spätere Bearbeitungen nicht ins Import-Packet zurückwirken.
            var localVermisstenIds = new HashSet<Guid>(
                (einsatz.Vermisste ?? new()).Select(v => v.Id));
            foreach (var vermisst in session.Packet.Vermisste ?? new())
            {
                if (localVermisstenIds.Contains(vermisst.Id)) continue;
                await _einsatzService.UpsertVermisstenAsync(DeepCloneVermisst(vermisst));
                historyEntry.AddedVermisstenIds.Add(vermisst.Id.ToString());
            }
        }

        private static VermisstenInfo DeepCloneVermisst(VermisstenInfo source)
        {
            var json = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<VermisstenInfo>(json)!;
        }

        private async Task ApplyToArchivedEinsatzAsync(
            EinsatzMergeSession session,
            Dictionary<string, string> remapping,
            MergeHistoryEntry historyEntry)
        {
            var archived = await _archivService.GetByIdAsync(session.TargetArchivedEinsatzId!);
            if (archived == null) throw new InvalidOperationException("Archivierter Einsatz nicht gefunden.");

            var localTrackIds = new HashSet<string>(archived.TrackSnapshots.Select(t => t.Id));
            var localNoteIds = new HashSet<string>(archived.GlobalNotesEntries.Select(n => n.Id));
            var localAreaIds = new HashSet<string>(archived.SearchAreas.Select(a => a.Id));

            foreach (var teamItem in session.TeamItems.Where(t => t.ShouldImport))
            {
                var archivedTeam = ArchivedTeam.FromTeam(teamItem.ImportedTeam);
                archivedTeam.MemberNames = teamItem.ResolvedMemberNames.ToList();
                archived.Teams.Add(archivedTeam);
                historyEntry.AddedTeamIds.Add(teamItem.ImportedTeam.TeamId);
            }

            foreach (var noteItem in session.NoteItems.Where(n => !n.IsAlreadyPresent && n.ShouldImport))
            {
                archived.GlobalNotesEntries.Add(noteItem.Note);
                historyEntry.AddedNoteIds.Add(noteItem.Note.Id);
            }

            foreach (var areaItem in session.SearchAreaItems.Where(
                a => a.ShouldImport && a.ConflictType != SearchAreaConflict.SameIdExists))
            {
                var area = areaItem.ImportedArea;

                if (!string.IsNullOrEmpty(area.AssignedTeamId) &&
                    remapping.TryGetValue(area.AssignedTeamId, out var mappedId))
                {
                    area.AssignedTeamId = mappedId;
                }

                if (areaItem.ConflictType == SearchAreaConflict.SameNameDifferentId)
                {
                    switch (areaItem.NameConflictResolution)
                    {
                        case SearchAreaNameConflictResolution.Rename:
                            area.Name = area.Name + SearchAreaRenameSuffix;
                            break;
                        case SearchAreaNameConflictResolution.ReplaceLocal:
                            archived.SearchAreas.RemoveAll(a => a.Id == areaItem.LocalConflictArea!.Id);
                            break;
                        case SearchAreaNameConflictResolution.KeepBoth:
                            area.Id = Guid.NewGuid().ToString();
                            break;
                    }
                }

                archived.SearchAreas.Add(area);
                historyEntry.AddedSearchAreaIds.Add(area.Id);
            }

            foreach (var track in session.Packet.TrackSnapshots.Where(t => !localTrackIds.Contains(t.Id)))
            {
                archived.TrackSnapshots.Add(track);
                historyEntry.AddedTrackSnapshotIds.Add(track.Id);
            }

            archived.Vermisste ??= new();
            var localArchivedVermisstenIds = new HashSet<Guid>(archived.Vermisste.Select(v => v.Id));
            foreach (var vermisst in session.Packet.Vermisste ?? new())
            {
                if (localArchivedVermisstenIds.Contains(vermisst.Id)) continue;
                archived.Vermisste.Add(DeepCloneVermisst(vermisst));
                historyEntry.AddedVermisstenIds.Add(vermisst.Id.ToString());
            }

            archived.LastMergedAt = Now;

            await _archivService.UpdateArchivedEinsatzAsync(archived);
        }

        private async Task PersistMergeHistoryEntryAsync(
            MergeHistoryEntry entry, string? archivedEinsatzId)
        {
            if (archivedEinsatzId == null)
            {
                _einsatzService.CurrentEinsatz.MergeHistory ??= new();
                _einsatzService.CurrentEinsatz.MergeHistory.Insert(0, entry);
            }
            else
            {
                var archived = await _archivService.GetByIdAsync(archivedEinsatzId);
                if (archived == null) return;

                archived.MergeHistory ??= new();
                archived.MergeHistory.Insert(0, entry);
                archived.LastMergedAt = entry.MergedAt;

                await _archivService.UpdateArchivedEinsatzAsync(archived);
            }
        }

        private async Task ApplyFieldOverwritesAsync(
            List<MasterDataMergeItem> items, Dictionary<string, string> remapping)
        {
            foreach (var item in items.Where(x =>
                x.Decision == MergeDecision.LinkToExisting &&
                x.OverwriteLocalFields &&
                !string.IsNullOrEmpty(x.SelectedLocalId)))
            {
                var local = await _masterDataService.GetPersonalByIdAsync(item.SelectedLocalId!);
                if (local == null) continue;

                var imported = (PersonalEntry)item.ImportedEntry;
                local.Vorname = imported.Vorname;
                local.Nachname = imported.Nachname;
                local.Skills = imported.Skills;
                if (!string.IsNullOrWhiteSpace(imported.Notizen))
                    local.Notizen = imported.Notizen;

                await _masterDataService.UpdatePersonalAsync(local);
            }
        }

        private async Task ApplyDogFieldOverwritesAsync(
            List<MasterDataMergeItem> items, Dictionary<string, string> remapping)
        {
            foreach (var item in items.Where(x =>
                x.Decision == MergeDecision.LinkToExisting &&
                x.OverwriteLocalFields &&
                !string.IsNullOrEmpty(x.SelectedLocalId)))
            {
                var local = await _masterDataService.GetDogByIdAsync(item.SelectedLocalId!);
                if (local == null) continue;

                var imported = (DogEntry)item.ImportedEntry;
                local.Name = imported.Name;
                local.Rasse = imported.Rasse;
                local.Specializations = imported.Specializations;
                if (!string.IsNullOrWhiteSpace(imported.Notizen))
                    local.Notizen = imported.Notizen;

                await _masterDataService.UpdateDogAsync(local);
            }
        }

        private async Task ApplyDroneFieldOverwritesAsync(
            List<MasterDataMergeItem> items, Dictionary<string, string> remapping)
        {
            foreach (var item in items.Where(x =>
                x.Decision == MergeDecision.LinkToExisting &&
                x.OverwriteLocalFields &&
                !string.IsNullOrEmpty(x.SelectedLocalId)))
            {
                var local = await _masterDataService.GetDroneByIdAsync(item.SelectedLocalId!);
                if (local == null) continue;

                var imported = (DroneEntry)item.ImportedEntry;
                local.Name = imported.Name;
                local.Modell = imported.Modell;
                local.Hersteller = imported.Hersteller;
                if (!string.IsNullOrWhiteSpace(imported.Notizen))
                    local.Notizen = imported.Notizen;

                await _masterDataService.UpdateDroneAsync(local);
            }
        }

        private static Team ApplyRemappingToTeam(Team team, Dictionary<string, string> remapping)
        {
            var t = ShallowCopyTeam(team);

            if (!string.IsNullOrEmpty(t.HundefuehrerId) &&
                remapping.TryGetValue(t.HundefuehrerId, out var hfId))
                t.HundefuehrerId = hfId;

            for (int i = 0; i < t.HelferIds.Count; i++)
            {
                if (!string.IsNullOrEmpty(t.HelferIds[i]) &&
                    remapping.TryGetValue(t.HelferIds[i], out var hId))
                    t.HelferIds[i] = hId;
            }

            if (!string.IsNullOrEmpty(t.DogId) &&
                remapping.TryGetValue(t.DogId, out var dId))
                t.DogId = dId;

            if (!string.IsNullOrEmpty(t.DroneId) &&
                remapping.TryGetValue(t.DroneId, out var drId))
                t.DroneId = drId;

            if (t.HundefuehrerId == string.Empty) { t.HundefuehrerName = string.Empty; }
            if (t.DogId == string.Empty) { t.DogName = string.Empty; }
            if (t.DroneId == string.Empty) { t.DroneType = string.Empty; }

            return t;
        }

        private static Team ShallowCopyTeam(Team src)
        {
            return new Team
            {
                TeamId = src.TeamId,
                TeamName = src.TeamName,
                DogName = src.DogName,
                DogId = src.DogId,
                DogSpecialization = src.DogSpecialization,
                Einsatzart = src.Einsatzart,
                HundefuehrerName = src.HundefuehrerName,
                HundefuehrerId = src.HundefuehrerId,
                HelferIds = new List<string>(src.HelferIds),
                HelferNames = new List<string>(src.HelferNames),
                SearchAreaName = src.SearchAreaName,
                SearchAreaId = src.SearchAreaId,
                ElapsedTime = src.ElapsedTime,
                IsRunning = false,
                FirstWarningMinutes = src.FirstWarningMinutes,
                SecondWarningMinutes = src.SecondWarningMinutes,
                CreatedAt = src.CreatedAt,
                Notes = src.Notes,
                IsDroneTeam = src.IsDroneTeam,
                DroneType = src.DroneType,
                DroneId = src.DroneId,
                IsSupportTeam = src.IsSupportTeam,
                CollarId = src.CollarId,
                CollarName = src.CollarName
            };
        }

        private static List<string> GetMemberNames(Team team)
        {
            var names = new List<string>();
            if (!string.IsNullOrEmpty(team.HundefuehrerName)) names.Add(team.HundefuehrerName);
            foreach (var h in team.HelferNames)
            {
                if (!string.IsNullOrEmpty(h)) names.Add(h);
            }
            return names;
        }

        private static List<string> GetResolvedMemberNames(Team team, Dictionary<string, string> remapping)
        {
            var names = new List<string>();
            if (!string.IsNullOrEmpty(team.HundefuehrerName)) names.Add(team.HundefuehrerName);
            foreach (var h in team.HelferNames)
            {
                if (!string.IsNullOrEmpty(h)) names.Add(h);
            }
            return names;
        }

        private async Task<(List<Team> teams, List<GlobalNotesEntry> notes, List<SearchArea> areas,
            List<MapMarker> markers, List<TeamTrackSnapshot> tracks)>
            GetLocalOperativeDataAsync(string? archivedEinsatzId)
        {
            if (archivedEinsatzId == null)
            {
                var einsatz = _einsatzService.CurrentEinsatz;
                return (
                    _einsatzService.Teams,
                    _einsatzService.GlobalNotes,
                    einsatz.SearchAreas,
                    await _einsatzService.GetMapMarkersAsync(),
                    einsatz.TrackSnapshots
                );
            }

            var archived = await _archivService.GetByIdAsync(archivedEinsatzId);
            if (archived == null)
                return (new(), new(), new(), new(), new());

            var teams = archived.Teams.Select(t => new Team
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName
            }).ToList();

            return (teams, archived.GlobalNotesEntries, archived.SearchAreas, new(), archived.TrackSnapshots);
        }
    }
}

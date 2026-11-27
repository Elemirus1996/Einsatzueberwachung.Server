using Einsatzueberwachung.Domain.Models;
using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Models.Merge;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzMergeService
    {
        private static MasterDataMergeItem BuildPersonalMergeItem(PersonalEntry imported, List<PersonalEntry> local)
        {
            var item = new MasterDataMergeItem
            {
                ImportedId = imported.Id,
                DisplayName = imported.FullName,
                DetailsDisplay = imported.SkillsShortDisplay,
                EntityType = MergeEntityType.Personal,
                ImportedEntry = imported
            };

            item.Suggestions = local
                .Select(p => ScorePersonal(imported, p))
                .Where(c => c.ConfidenceScore > 0)
                .OrderByDescending(c => c.ConfidenceScore)
                .Take(5)
                .ToList();

            var localById = local.ToDictionary(p => p.Id);
            var bestNameMatch = item.Suggestions
                .Select(c => localById.TryGetValue(c.LocalId, out var lp) ? lp : null)
                .FirstOrDefault(p => p != null &&
                    string.Equals(imported.Vorname, p.Vorname, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(imported.Nachname, p.Nachname, StringComparison.OrdinalIgnoreCase));

            if (bestNameMatch != null)
            {
                item.Decision = MergeDecision.LinkToExisting;
                item.SelectedLocalId = bestNameMatch.Id;
            }

            return item;
        }

        private static MasterDataMergeItem BuildDogMergeItem(
            DogEntry imported,
            List<DogEntry> local,
            List<PersonalEntry> importedPersonal,
            List<PersonalEntry> localPersonal)
        {
            var item = new MasterDataMergeItem
            {
                ImportedId = imported.Id,
                DisplayName = imported.Name,
                DetailsDisplay = imported.SpecializationsShortDisplay,
                EntityType = MergeEntityType.Dog,
                ImportedEntry = imported
            };

            item.Suggestions = local
                .Select(d => ScoreDog(imported, d))
                .Where(c => c.ConfidenceScore > 0)
                .OrderByDescending(c => c.ConfidenceScore)
                .Take(5)
                .ToList();

            var importedHandlerNames = ResolveHandlerNames(imported.HundefuehrerIds, importedPersonal);
            if (importedHandlerNames.Count > 0)
            {
                var localById = local.ToDictionary(d => d.Id);
                var bestMatch = item.Suggestions
                    .Select(c => localById.TryGetValue(c.LocalId, out var ld) ? ld : null)
                    .FirstOrDefault(d => d != null &&
                        string.Equals(imported.Name, d.Name, StringComparison.OrdinalIgnoreCase) &&
                        ResolveHandlerNames(d.HundefuehrerIds, localPersonal)
                            .Overlaps(importedHandlerNames));

                if (bestMatch != null)
                {
                    item.Decision = MergeDecision.LinkToExisting;
                    item.SelectedLocalId = bestMatch.Id;
                }
            }

            if (item.Decision == MergeDecision.Undecided && item.Suggestions.Count > 0)
            {
                var best = item.Suggestions[0];
                if (best.ConfidenceScore >= 1.0 &&
                    string.Equals(imported.Name, best.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    item.Decision = MergeDecision.LinkToExisting;
                    item.SelectedLocalId = best.LocalId;
                }
            }

            return item;
        }

        private static HashSet<string> ResolveHandlerNames(
            IEnumerable<string> handlerIds,
            List<PersonalEntry> personal)
        {
            var byId = personal.ToDictionary(p => p.Id);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in handlerIds)
            {
                if (byId.TryGetValue(id, out var p))
                    names.Add(p.FullName);
            }
            return names;
        }

        private static MasterDataMergeItem BuildDroneMergeItem(DroneEntry imported, List<DroneEntry> local)
        {
            var item = new MasterDataMergeItem
            {
                ImportedId = imported.Id,
                DisplayName = imported.DisplayName,
                DetailsDisplay = imported.FullDescription,
                EntityType = MergeEntityType.Drone,
                ImportedEntry = imported
            };

            item.Suggestions = local
                .Select(d => ScoreDrone(imported, d))
                .Where(c => c.ConfidenceScore > 0)
                .OrderByDescending(c => c.ConfidenceScore)
                .Take(5)
                .ToList();

            return item;
        }

        private static MasterDataMergeCandidate ScorePersonal(PersonalEntry imported, PersonalEntry local)
        {
            double score = 0;
            string reason = "PARTIAL";
            string label = "Ähnlicher Name";

            var importedName = imported.FullName.ToLowerInvariant();
            var localName = local.FullName.ToLowerInvariant();
            bool exactName = importedName == localName;
            bool partialName = !exactName && ContainsPartialName(importedName, localName);

            if (imported.Id == local.Id)
            {
                score += (exactName || partialName) ? 0.85 : 0.15;
                reason = "SAME_ID";
                label = "Gleiche ID";
            }

            if (exactName)
            {
                score += 0.70;
                if (reason != "SAME_ID") { reason = "EXACT_NAME"; label = "Gleicher Name"; }
            }
            else if (partialName)
            {
                score += 0.35;
                if (reason == "PARTIAL") { reason = "PARTIAL_NAME"; label = "Ähnlicher Name"; }
            }

            if (imported.Skills != PersonalSkills.None && imported.Skills == local.Skills)
            {
                score += 0.05;
            }

            if (score <= 0) return new MasterDataMergeCandidate { ConfidenceScore = 0 };

            return new MasterDataMergeCandidate
            {
                LocalId = local.Id,
                DisplayName = local.FullName,
                MatchReason = reason,
                MatchReasonLabel = label,
                ConfidenceScore = Math.Min(score, 1.0),
                DetailsDisplay = local.SkillsShortDisplay
            };
        }

        private static MasterDataMergeCandidate ScoreDog(DogEntry imported, DogEntry local)
        {
            double score = 0;
            string reason = "PARTIAL";
            string label = "Ähnlicher Name";

            var importedName = imported.Name.ToLowerInvariant();
            var localName = local.Name.ToLowerInvariant();
            bool exactName = importedName == localName;
            bool partialName = !exactName && ContainsPartialName(importedName, localName);

            if (imported.Id == local.Id)
            {
                score += (exactName || partialName) ? 0.85 : 0.15;
                reason = "SAME_ID";
                label = "Gleiche ID";
            }

            if (exactName)
            {
                score += 0.70;
                if (reason != "SAME_ID") { reason = "EXACT_NAME"; label = "Gleicher Name"; }
            }
            else if (partialName)
            {
                score += 0.35;
                if (reason == "PARTIAL") { reason = "PARTIAL_NAME"; label = "Ähnlicher Name"; }
            }

            if (imported.Specializations != DogSpecialization.None &&
                imported.Specializations == local.Specializations)
            {
                score += 0.05;
            }

            if (score <= 0) return new MasterDataMergeCandidate { ConfidenceScore = 0 };

            return new MasterDataMergeCandidate
            {
                LocalId = local.Id,
                DisplayName = local.Name,
                MatchReason = reason,
                MatchReasonLabel = label,
                ConfidenceScore = Math.Min(score, 1.0),
                DetailsDisplay = local.SpecializationsShortDisplay
            };
        }

        private static MasterDataMergeCandidate ScoreDrone(DroneEntry imported, DroneEntry local)
        {
            double score = 0;
            string reason = "PARTIAL";
            string label = "Ähnlicher Name";

            var importedName = imported.DisplayName.ToLowerInvariant();
            var localName = local.DisplayName.ToLowerInvariant();
            bool exactName = importedName == localName;
            bool partialName = !exactName && ContainsPartialName(importedName, localName);

            if (imported.Id == local.Id)
            {
                score += (exactName || partialName) ? 0.85 : 0.15;
                reason = "SAME_ID";
                label = "Gleiche ID";
            }

            if (exactName)
            {
                score += 0.70;
                if (reason != "SAME_ID") { reason = "EXACT_NAME"; label = "Gleicher Name"; }
            }
            else if (partialName)
            {
                score += 0.35;
                if (reason == "PARTIAL") { reason = "PARTIAL_NAME"; label = "Ähnlicher Name"; }
            }

            if (imported.Seriennummer == local.Seriennummer &&
                !string.IsNullOrEmpty(imported.Seriennummer))
            {
                score += 0.10;
                if (reason == "PARTIAL") { reason = "SERIAL"; label = "Gleiche Seriennummer"; }
            }

            if (score <= 0) return new MasterDataMergeCandidate { ConfidenceScore = 0 };

            return new MasterDataMergeCandidate
            {
                LocalId = local.Id,
                DisplayName = local.DisplayName,
                MatchReason = reason,
                MatchReasonLabel = label,
                ConfidenceScore = Math.Min(score, 1.0),
                DetailsDisplay = local.FullDescription
            };
        }

        private static bool ContainsPartialName(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                   b.Contains(a, StringComparison.OrdinalIgnoreCase);
        }

        private static PersonalEntry ClonePersonal(PersonalEntry src)
        {
            return new PersonalEntry
            {
                Id = Guid.NewGuid().ToString(),
                Vorname = src.Vorname,
                Nachname = src.Nachname,
                Organisation = src.Organisation,
                Skills = src.Skills,
                Notizen = src.Notizen,
                IsActive = src.IsActive,
                DiveraUserId = src.DiveraUserId
            };
        }

        private static DogEntry CloneDog(DogEntry src)
        {
            return new DogEntry
            {
                Id = Guid.NewGuid().ToString(),
                Name = src.Name,
                Rasse = src.Rasse,
                Organisation = src.Organisation,
                Alter = src.Alter,
                Specializations = src.Specializations,
                HundefuehrerIds = new List<string>(src.HundefuehrerIds),
                Notizen = src.Notizen,
                IsActive = src.IsActive
            };
        }

        private static DroneEntry CloneDrone(DroneEntry src)
        {
            return new DroneEntry
            {
                Id = Guid.NewGuid().ToString(),
                Name = src.Name,
                Modell = src.Modell,
                Hersteller = src.Hersteller,
                Seriennummer = src.Seriennummer,
                DrohnenpilotId = src.DrohnenpilotId,
                Notizen = src.Notizen,
                IsActive = src.IsActive
            };
        }
    }
}

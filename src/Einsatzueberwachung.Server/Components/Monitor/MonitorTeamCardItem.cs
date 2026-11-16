using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Server.Components.Monitor;

public sealed record MonitorTeamCardItem(
    Team Team,
    bool IsDogConflict,
    string? ConflictingTeamName,
    bool IsExpanded,
    CollarLocation? LastCollarLocation,
    TeamPhoneLocation? LastPhoneLocation,
    IReadOnlyList<GlobalNotesEntry> LatestNoteEntries,
    IReadOnlyList<TeamTrackSnapshot> CompletedSearches);

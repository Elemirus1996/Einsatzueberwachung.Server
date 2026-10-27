// Repräsentiert eine abgeschlossene Suchepisode eines Teams.
// Eine Suche kann maximal einen Halsband-Track (CollarTrack) und einen Mensch-Laufweg (HumanTrack) enthalten.

using Einsatzueberwachung.Domain.Models.Enums;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>
    /// Repräsentiert eine abgeschlossene Suchepisode eines Teams.
    /// Kann maximal einen Halsband-Track (CollarTrack) und einen Mensch-Laufweg (HumanTrack) enthalten.
    /// </summary>
    public class CompletedSearch
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string TeamId { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public DateTime SearchStart { get; set; }
        public DateTime SearchEnd { get; set; }
        public string? SearchAreaId { get; set; }
        public string? SearchAreaName { get; set; }

        /// <summary>
        /// GPS-Tracks dieser Suche. Maximal 1× CollarTrack und 1× HumanTrack.
        /// </summary>
        public List<TeamTrackSnapshot> Tracks { get; set; } = new();

        /// <summary>Gibt an, ob noch kein CollarTrack vorhanden ist und einer hinzugefügt werden kann.</summary>
        public bool CanAddCollarTrack => Tracks.All(t => t.TrackType != TrackType.CollarTrack);

        /// <summary>Gibt an, ob noch kein HumanTrack vorhanden ist und einer hinzugefügt werden kann.</summary>
        public bool CanAddHumanTrack => Tracks.All(t => t.TrackType != TrackType.HumanTrack);

        public string FormattedTimeRange =>
            $"{Ortszeit.AusUtc(SearchStart):dd.MM.yyyy HH:mm} – {Ortszeit.AusUtc(SearchEnd):HH:mm}";
    }
}

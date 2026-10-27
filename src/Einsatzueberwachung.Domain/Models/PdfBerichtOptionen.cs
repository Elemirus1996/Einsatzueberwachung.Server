using System.Collections.Generic;
using System.Linq;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>
    /// Auswahl fuer den Einsatzbericht (PDF): GPS-Tracks ja/nein, Kartenart der Track- und Abdeckungskarten und
    /// welche Teams in Tracks und Abdeckung erscheinen (null = alle).
    /// </summary>
    public sealed record PdfBerichtOptionen(
        bool Tracks = true,
        MapTileType Kartenart = MapTileType.Streets,
        IReadOnlyCollection<string>? TeamIds = null)
    {
        public static PdfBerichtOptionen OhneTracks { get; } = new(Tracks: false);

        /// <summary>Kartenarten, die im Bericht angeboten werden (Google bewusst nicht – Nutzungsbedingungen)</summary>
        public static IReadOnlyList<(MapTileType Art, string Name)> Kartenarten { get; } =
        [
            (MapTileType.Streets, "Straßenkarte"),
            (MapTileType.Topographic, "Topografisch"),
            (MapTileType.Satellite, "Satellit (Esri)")
        ];

        public List<TeamTrackSnapshot>? WaehleTracks(IEnumerable<TeamTrackSnapshot>? alle)
        {
            if (!Tracks || alle is null) return null;
            return TeamIds is null ? alle.ToList() : alle.Where(t => TeamIds.Contains(t.TeamId)).ToList();
        }
    }
}

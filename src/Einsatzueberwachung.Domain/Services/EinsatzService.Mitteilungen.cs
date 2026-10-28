using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Domain.Services
{
    public partial class EinsatzService
    {
        /// <summary>Je Team gespeicherte Mitteilungen der Einsatzleitung (aelteste fallen weg)</summary>
        public const int MaxTeamMitteilungen = 20;

        private readonly object _mitteilungenLock = new();

        public event Action<TeamMitteilung>? TeamMitteilungGespeichert;

        public TeamMitteilung TeamMitteilungSpeichern(string teamId, string text)
        {
            var mitteilung = new TeamMitteilung { TeamId = teamId, Text = text.Trim(), ZeitUtc = DateTime.UtcNow };
            lock (_mitteilungenLock)
            {
                var liste = _currentEinsatz.TeamMitteilungen;
                liste.Add(mitteilung);
                var zuViel = liste.Count(m => m.TeamId == teamId) - MaxTeamMitteilungen;
                for (var i = 0; zuViel > 0 && i < liste.Count; )
                {
                    if (liste[i].TeamId == teamId) { liste.RemoveAt(i); zuViel--; }
                    else i++;
                }
            }
            TeamMitteilungGespeichert?.Invoke(mitteilung);
            return mitteilung;
        }

        public IReadOnlyList<TeamMitteilung> TeamMitteilungen(string teamId)
        {
            lock (_mitteilungenLock)
            {
                return _currentEinsatz.TeamMitteilungen.Where(m => m.TeamId == teamId).OrderBy(m => m.ZeitUtc).ToList();
            }
        }
    }
}

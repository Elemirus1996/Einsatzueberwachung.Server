namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>Mitteilung der Einsatzleitung an ein Team (Monitor → Team-Seite/App), gespeichert fuer die App (#119).</summary>
    public class TeamMitteilung
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string TeamId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        /// <summary>Zeitpunkt in UTC</summary>
        public DateTime ZeitUtc { get; set; }
    }
}

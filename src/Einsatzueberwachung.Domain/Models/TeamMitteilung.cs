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
        /// <summary>Kartenpunkt als Navigationsziel (Variante C); null = normale Mitteilung</summary>
        public NavigationsZiel? Ziel { get; set; }
    }

    /// <summary>Schnappschuss eines Kartenpunkts, den die Einsatzleitung einem Team als Ziel schickt</summary>
    public class NavigationsZiel
    {
        public string PunktId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}

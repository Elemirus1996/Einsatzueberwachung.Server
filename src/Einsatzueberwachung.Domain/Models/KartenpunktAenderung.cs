namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>Art der Aenderung eines Kartenpunkts, wie im EL-Ereignis „punkt“ (docs/APP_API.md)</summary>
    public static class KartenpunktAenderung
    {
        public const string Neu = "neu";
        public const string Geaendert = "geaendert";
        public const string Geloescht = "geloescht";
    }
}

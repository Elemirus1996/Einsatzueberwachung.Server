namespace Einsatzueberwachung.Domain.Models.Divera
{
    /// <summary>Ergebnis beim Senden eines Fotos in den Messenger-Chat des Divera-Einsatzes</summary>
    public sealed record DiveraFotoErgebnis(bool Erfolg, string Meldung)
    {
        public static DiveraFotoErgebnis Ok() => new(true, "Foto im Divera-Einsatz-Chat.");
        public static DiveraFotoErgebnis Fehler(string meldung) => new(false, meldung);
    }
}

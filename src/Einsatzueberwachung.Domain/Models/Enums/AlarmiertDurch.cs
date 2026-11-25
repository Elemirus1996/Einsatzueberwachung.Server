// Wer die Staffel alarmiert hat (Pflichtfrage beim Anlegen, Entscheidung Paul 17.11.2026). Die Stelle bzw. der Name
// steht zusätzlich als Freitext in EinsatzData.Alarmiert. Polizeiangaben im Abschluss nur bei Polizei.

namespace Einsatzueberwachung.Domain.Models.Enums
{
    public enum AlarmiertDurch
    {
        Polizei,
        Feuerwehr,
        Rettungsdienst,
        AndereStaffel
    }

    public static class AlarmiertDurchExtensions
    {
        public static readonly AlarmiertDurch[] Alle =
            { AlarmiertDurch.Polizei, AlarmiertDurch.Feuerwehr, AlarmiertDurch.Rettungsdienst, AlarmiertDurch.AndereStaffel };

        public static string Anzeige(this AlarmiertDurch art) => art switch
        {
            AlarmiertDurch.Polizei => "Polizei",
            AlarmiertDurch.Feuerwehr => "Feuerwehr",
            AlarmiertDurch.Rettungsdienst => "Rettungsdienst",
            AlarmiertDurch.AndereStaffel => "andere Rettungshundestaffel",
            _ => art.ToString()
        };

        public static AlarmiertDurch? AusApiWert(string? wert) =>
            Enum.TryParse<AlarmiertDurch>(wert, ignoreCase: false, out var art) && Alle.Contains(art) ? art : null;
    }
}

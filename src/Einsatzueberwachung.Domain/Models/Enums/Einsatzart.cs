// Wie ein Team im Einsatz eingesetzt ist („Eingesetzt als“). Wird im Monitor-Teamdialog gewählt und beim Start jeder Suche
// an der Suche festgehalten, damit der Einsatzbericht je Suche die Art kennt.

namespace Einsatzueberwachung.Domain.Models.Enums
{
    public enum Einsatzart
    {
        Flaeche,
        Truemmer,
        Mantrailer,
        Wasser,
        Lawine
    }

    public static class EinsatzartExtensions
    {
        public static readonly Einsatzart[] Alle =
            { Einsatzart.Flaeche, Einsatzart.Truemmer, Einsatzart.Mantrailer, Einsatzart.Wasser, Einsatzart.Lawine };

        public static string Anzeige(this Einsatzart art) => art switch
        {
            Einsatzart.Flaeche => "Fläche",
            Einsatzart.Truemmer => "Trümmer",
            Einsatzart.Mantrailer => "Mantrailer",
            Einsatzart.Wasser => "Wasser",
            Einsatzart.Lawine => "Lawine",
            _ => art.ToString()
        };

        /// <summary>Wert in der App-Schnittstelle (wie der Enum-Name).</summary>
        public static string ApiWert(this Einsatzart art) => art.ToString();

        public static Einsatzart? AusApiWert(string? wert) =>
            Enum.TryParse<Einsatzart>(wert, ignoreCase: false, out var art) && Alle.Contains(art) ? art : null;

        public static DogSpecialization AlsSpezialisierung(this Einsatzart art) => art switch
        {
            Einsatzart.Flaeche => DogSpecialization.Flaechensuche,
            Einsatzart.Truemmer => DogSpecialization.Truemmersuche,
            Einsatzart.Mantrailer => DogSpecialization.Mantrailing,
            Einsatzart.Wasser => DogSpecialization.Wasserortung,
            Einsatzart.Lawine => DogSpecialization.Lawinensuche,
            _ => DogSpecialization.None
        };

        /// <summary>Einsatzarten, die der Hund laut Stammdaten kann. Leer = keine passende Spezialisierung hinterlegt.</summary>
        public static List<Einsatzart> AusSpezialisierungen(DogSpecialization spezialisierungen) =>
            Alle.Where(a => spezialisierungen.HasFlag(a.AlsSpezialisierung())).ToList();

        /// <summary>Vorauswahl: genau eine passende Spezialisierung, sonst keine.</summary>
        public static Einsatzart? Vorauswahl(DogSpecialization spezialisierungen)
        {
            var arten = AusSpezialisierungen(spezialisierungen);
            return arten.Count == 1 ? arten[0] : null;
        }
    }
}

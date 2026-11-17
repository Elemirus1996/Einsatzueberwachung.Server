namespace Einsatzueberwachung.Domain.Services
{
    /// <summary>
    /// Anzeige des Einsatz-Ergebnisses. Gespeichert bleibt der alte Wert ohne Umlaut („Uebung abgeschlossen“),
    /// damit Archiv-Filter, Statistik und Pruefungen weiter passen (#90).
    /// </summary>
    public static class ErgebnisText
    {
        public static string Anzeige(string? ergebnis) =>
            string.IsNullOrEmpty(ergebnis) ? ergebnis ?? string.Empty : ergebnis.Replace("Uebung", "Übung").Replace(" - ", " – ");

        /// <summary>Ergebnis mit Fund der Person: Beim Einsatzende werden Fundzeit, Fundort und Übergabe abgefragt.</summary>
        public static bool IstFund(string? ergebnis) =>
            ergebnis?.Contains("gefunden", StringComparison.OrdinalIgnoreCase) == true;
    }
}

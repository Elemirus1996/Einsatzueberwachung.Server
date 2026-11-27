using Einsatzueberwachung.Domain.Models;

namespace Einsatzueberwachung.Server.Services;

/// <summary>
/// Rolle der EL-App je Einsatz (APP_API EL Abschnitt 4a, Entscheidung Paul 17.11.2026): Einsatzleiter steuert und sendet
/// den Standort, Fuehrungsassistent steuert ohne Standort, alle anderen lesen nur. Ohne gewaehlte Person (aeltere Apps)
/// wie bisher Einsatzleiter.
/// </summary>
public static class ElRolle
{
    public const string Einsatzleiter = "einsatzleiter";
    public const string Fuehrungsassistent = "fuehrungsassistent";
    public const string Lesen = "lesen";

    /// <param name="einsatz">Aktueller Einsatz für Status- und Rollenabgleich</param>
    /// <param name="personName">Name der gewaehlten Person; null = keine Person gewaehlt</param>
    /// <param name="personGewaehlt">true, wenn am Gerät oder im Token eine Person hinterlegt ist</param>
    public static string Bestimmen(EinsatzData einsatz, string? personName, bool personGewaehlt)
    {
        if (!personGewaehlt) return Einsatzleiter;
        if (!ElPositionService.Laeuft(einsatz) || string.IsNullOrWhiteSpace(personName)) return Lesen;
        if (Gleich(einsatz.Einsatzleiter, personName)) return Einsatzleiter;
        if (Gleich(einsatz.Fuehrungsassistent, personName)) return Fuehrungsassistent;
        return Lesen;
    }

    public static bool DarfSteuern(string rolle) => rolle != Lesen;

    public static bool SendetStandort(string rolle) => rolle == Einsatzleiter;

    private static bool Gleich(string? a, string b) =>
        string.Equals(a?.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

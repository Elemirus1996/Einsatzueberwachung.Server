using System;

namespace Einsatzueberwachung.Domain.Models.Enums
{
    public enum EinsatzSzenarioType
    {
        Unbestimmt = 0,
        Mantrailer = 1,
        Flaeche = 2,
        Truemmer = 3,
        Sonstige = 4
    }

    public static class EinsatzSzenarioTypeExtensions
    {
        public static string GetDisplayName(this EinsatzSzenarioType szenario) => szenario switch
        {
            EinsatzSzenarioType.Mantrailer => "Mantrailer",
            EinsatzSzenarioType.Flaeche => "Flächensuche",
            EinsatzSzenarioType.Truemmer => "Trümmersuche",
            EinsatzSzenarioType.Sonstige => "Sonstige",
            _ => "Unbestimmt"
        };

        public static string GetShortName(this EinsatzSzenarioType szenario) => szenario switch
        {
            EinsatzSzenarioType.Mantrailer => "MT",
            EinsatzSzenarioType.Flaeche => "FL",
            EinsatzSzenarioType.Truemmer => "TR",
            EinsatzSzenarioType.Sonstige => "SO",
            _ => "?"
        };

        public static string GetColorHex(this EinsatzSzenarioType szenario) => szenario switch
        {
            EinsatzSzenarioType.Mantrailer => "var(--success-color)",
            EinsatzSzenarioType.Flaeche => "var(--info-color)",
            EinsatzSzenarioType.Truemmer => "var(--warning-color)",
            EinsatzSzenarioType.Sonstige => "var(--ui-muted)",
            _ => "var(--ui-muted)"
        };

        public static string GetDescription(this EinsatzSzenarioType szenario) => szenario switch
        {
            EinsatzSzenarioType.Mantrailer => "Personensuche auf Geruchsspur, in der Regel eine vermisste Person.",
            EinsatzSzenarioType.Flaeche => "Großflächiges Absuchen eines Gebietes, ggf. mehrere Vermisste.",
            EinsatzSzenarioType.Truemmer => "Suche im eingestürzten Bauwerk/Trümmerfeld, ggf. mehrere Vermisste, Lagekarte ohne GPS.",
            EinsatzSzenarioType.Sonstige => "Sonstiges Szenario, das nicht in die anderen Kategorien fällt.",
            _ => "Szenario noch nicht festgelegt."
        };

        public static bool AllowsMultipleVermisste(this EinsatzSzenarioType szenario) =>
            szenario == EinsatzSzenarioType.Flaeche || szenario == EinsatzSzenarioType.Truemmer;

        public static string GetIconClass(this EinsatzSzenarioType szenario) => szenario switch
        {
            EinsatzSzenarioType.Mantrailer => "bi-signpost-split",
            EinsatzSzenarioType.Flaeche => "bi-bounding-box",
            EinsatzSzenarioType.Truemmer => "bi-bricks",
            EinsatzSzenarioType.Sonstige => "bi-three-dots",
            _ => "bi-question-circle"
        };
    }
}

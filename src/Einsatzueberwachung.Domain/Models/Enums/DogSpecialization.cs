// Quelle: WPF-Projekt Models/DogSpecialization.cs
// Beschreibt die Ausbildungen/Spezialisierungen der Hunde (Fläche, Trümmer, Mantrailer, etc.)

using System;

namespace Einsatzueberwachung.Domain.Models.Enums
{
    [Flags]
    public enum DogSpecialization
    {
        None = 0,
        Flaechensuche = 1 << 0,        // 1
        Truemmersuche = 1 << 1,        // 2
        Mantrailing = 1 << 2,          // 4
        Wasserortung = 1 << 3,         // 8
        Lawinensuche = 1 << 4,         // 16
        Gelaendesuche = 1 << 5,        // 32
        Leichensuche = 1 << 6,         // 64
        InAusbildung = 1 << 7          // 128
    }

    public static class DogSpecializationExtensions
    {
        public static string GetDisplayName(this DogSpecialization spec)
        {
            return spec switch
            {
                DogSpecialization.Flaechensuche => "Flächensuchhund",
                DogSpecialization.Truemmersuche => "Trümmersuchhund",
                DogSpecialization.Mantrailing => "Mantrailer",
                DogSpecialization.Wasserortung => "Wasserortung",
                DogSpecialization.Lawinensuche => "Lawinensuchhund",
                DogSpecialization.Gelaendesuche => "Geländesuchhund",
                DogSpecialization.Leichensuche => "Leichenspürhund",
                DogSpecialization.InAusbildung => "In Ausbildung",
                _ => spec.ToString()
            };
        }

        public static string GetShortName(this DogSpecialization spec)
        {
            return spec switch
            {
                DogSpecialization.Flaechensuche => "FL",
                DogSpecialization.Truemmersuche => "TR",
                DogSpecialization.Mantrailing => "MT",
                DogSpecialization.Wasserortung => "WO",
                DogSpecialization.Lawinensuche => "LA",
                DogSpecialization.Gelaendesuche => "GE",
                DogSpecialization.Leichensuche => "LS",
                DogSpecialization.InAusbildung => "IA",
                _ => ""
            };
        }

        public static string GetColorHex(this DogSpecialization spec)
        {
            if (spec.HasFlag(DogSpecialization.Flaechensuche))
                return "var(--info-color)";
            if (spec.HasFlag(DogSpecialization.Truemmersuche))
                return "var(--warning-color)";
            if (spec.HasFlag(DogSpecialization.Mantrailing))
                return "var(--success-color)";
            if (spec.HasFlag(DogSpecialization.Wasserortung))
                return "var(--theme-primary)";
            if (spec.HasFlag(DogSpecialization.Lawinensuche))
                return "var(--theme-secondary)";
            if (spec.HasFlag(DogSpecialization.Gelaendesuche))
                return "var(--success-color)";
            if (spec.HasFlag(DogSpecialization.Leichensuche))
                return "var(--ui-muted)";
            if (spec.HasFlag(DogSpecialization.InAusbildung))
                return "var(--warning-color)";
            
            return "#9E9E9E";
        }
    }
}

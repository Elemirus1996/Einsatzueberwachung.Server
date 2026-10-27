using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>
    /// Describes a warning source that is registered in code.
    /// Used to populate the Warnzentrum configuration UI.
    /// </summary>
    public class WarningRuleDefinition
    {
        /// <summary>Source identifier — must match the <see cref="WarningEntry.Source"/> value used in code.</summary>
        public string Source { get; init; } = string.Empty;

        /// <summary>Short human-readable name shown in the UI.</summary>
        public string Label { get; init; } = string.Empty;

        /// <summary>Longer description explaining when this warning fires.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>Default warning level as coded at the call-site.</summary>
        public WarningLevel DefaultLevel { get; init; } = WarningLevel.Warning;

        /// <summary>All warning sources that exist in the application.</summary>
        public static IReadOnlyList<WarningRuleDefinition> All { get; } =
        [
            new WarningRuleDefinition
            {
                Source = Sources.TeamTimer,
                Label = "Team-Timer: erste Warnstufe",
                Description = "Wird ausgelöst, wenn ein Team die erste Warnstufe (konfigurierbare Minuten) überschreitet.",
                DefaultLevel = WarningLevel.Warning
            },
            new WarningRuleDefinition
            {
                Source = Sources.TeamTimerCritical,
                Label = "Team-Timer: kritische Warnstufe",
                Description = "Wird ausgelöst, wenn ein Team die zweite/kritische Warnstufe überschreitet. Erfordert sofortige Aufmerksamkeit.",
                DefaultLevel = WarningLevel.Critical
            },
            new WarningRuleDefinition
            {
                Source = Sources.DogPause,
                Label = "Hund braucht Pause",
                Description = "Wird ausgelöst, wenn ein Hundeteam die konfigurierte Arbeitsdauer überschritten hat und eine Pflichtpause benötigt.",
                DefaultLevel = WarningLevel.Warning
            },
            new WarningRuleDefinition
            {
                Source = Sources.CollarOutOfBounds,
                Label = "Hund hat Suchgebiet verlassen",
                Description = "Wird ausgelöst, wenn ein GPS-Halsband das zugewiesene Suchgebiet des Teams verlässt.",
                DefaultLevel = WarningLevel.Critical
            },
            new WarningRuleDefinition
            {
                Source = Sources.CollarNoSignal,
                Label = "Kein Signal vom Halsband",
                Description = "Wird ausgelöst, wenn ein zugewiesenes Halsband während aktiver Suche länger als der konfigurierte Zeitraum kein Signal sendet.",
                DefaultLevel = WarningLevel.Critical
            },
            new WarningRuleDefinition
            {
                Source = Sources.CollarLowBattery,
                Label = "Halsband-Akku niedrig",
                Description = "Wird ausgelöst, wenn ein zugewiesenes Halsband während aktiver Suche den Akkustand 1/3 meldet.",
                DefaultLevel = WarningLevel.Warning
            },
            new WarningRuleDefinition
            {
                Source = Sources.PhoneNoSignal,
                Label = "Handy des Teams sendet nicht",
                Description = "Wird ausgelöst, wenn das Handy eines Teams (Traccar oder Team-Seite) während aktiver Suche länger als das eingestellte Zeitfenster keine Position schickt – nur bei Teams, deren Handy im Einsatz schon gesendet hat.",
                DefaultLevel = WarningLevel.Warning
            },
            new WarningRuleDefinition
            {
                Source = Sources.PhoneLowBattery,
                Label = "Handy-Akku niedrig",
                Description = "Wird ausgelöst, wenn das Handy eines Teams während aktiver Suche weniger als 20 % Akku meldet (Traccar Client).",
                DefaultLevel = WarningLevel.Warning
            },
            new WarningRuleDefinition
            {
                Source = Sources.ServerCertificate,
                Label = "Zertifikat der öffentlichen Adresse",
                Description = "Wird ausgelöst, wenn das Zertifikat der öffentlichen Adresse ungültig ist oder in weniger als 14 Tagen abläuft – ohne gültiges Zertifikat senden Traccar und Team-Handys nichts.",
                DefaultLevel = WarningLevel.Critical
            }
        ];

        /// <summary>Canonical source identifier constants — use these instead of string literals.</summary>
        public static class Sources
        {
            public const string TeamTimer         = "TeamTimer";
            public const string TeamTimerCritical = "TeamTimerCritical";
            public const string DogPause          = "DogPause";
            public const string CollarOutOfBounds = "CollarOutOfBounds";
            public const string CollarNoSignal    = "CollarNoSignal";
            public const string CollarLowBattery  = "CollarLowBattery";
            public const string PhoneNoSignal     = "PhoneNoSignal";
            public const string PhoneLowBattery   = "PhoneLowBattery";
            public const string ServerCertificate = "ServerCertificate";
        }
    }
}

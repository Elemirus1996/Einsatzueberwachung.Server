namespace Einsatzueberwachung.Domain.Models
{
    /// <summary>
    /// Einstellungen die pro Browser (localStorage) gespeichert werden.
    /// Jeder Browser hat seine eigenen Werte; die gemeinsamen Einsatzdaten
    /// bleiben auf dem Server.
    /// </summary>
    public class BrowserPreferences
    {
        // --- Theme ---
        public string ThemeMode { get; set; } = "Manual"; // "Manual" | "Auto" | "Scheduled"
        public bool IsDarkMode { get; set; } = false;
        public string DarkModeStartTime { get; set; } = "20:00"; // HH:mm
        public string DarkModeEndTime { get; set; } = "06:00";   // HH:mm

        // --- Sound ---
        public bool SoundAlertsEnabled { get; set; } = true;
        public int SoundVolume { get; set; } = 70; // 0-100
        public string FirstWarningSound { get; set; } = "beep";
        public string SecondWarningSound { get; set; } = "alarm";
        public int FirstWarningFrequency { get; set; } = 800;
        public int SecondWarningFrequency { get; set; } = 1200;
        public bool RepeatSecondWarning { get; set; } = true;
        public int RepeatWarningIntervalSeconds { get; set; } = 30;
    }
}

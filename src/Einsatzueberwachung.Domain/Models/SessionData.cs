// Quelle: WPF-Projekt Models/SessionData.cs
// Session-/Stammdaten für die Anwendung (Personal, Hunde, Einstellungen)

using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models
{
    public class SessionData
    {
        public List<PersonalEntry> PersonalList { get; set; }
        public List<DogEntry> DogList { get; set; }
        public List<DroneEntry> DroneList { get; set; }
        public StaffelSettings StaffelSettings { get; set; }
        public AppSettings AppSettings { get; set; }

        public SessionData()
        {
            PersonalList = new List<PersonalEntry>();
            DogList = new List<DogEntry>();
            DroneList = new List<DroneEntry>();
            StaffelSettings = new StaffelSettings();
            AppSettings = new AppSettings();
        }
    }

    public class StaffelSettings
    {
        public string StaffelName { get; set; }
        public string StaffelAdresse { get; set; }
        public string StaffelTelefon { get; set; }
        public string StaffelEmail { get; set; }
        public string StaffelLogoPfad { get; set; }

        public StaffelSettings()
        {
            StaffelName = string.Empty;
            StaffelAdresse = string.Empty;
            StaffelTelefon = string.Empty;
            StaffelEmail = string.Empty;
            StaffelLogoPfad = string.Empty;
        }
    }

    public class AppSettings
    {
        public string Theme { get; set; }
        public bool IsDarkMode { get; set; }
        public string ThemeMode { get; set; } // "Manual", "Auto", "Scheduled"
        public TimeSpan DarkModeStartTime { get; set; }
        public TimeSpan DarkModeEndTime { get; set; }
        public int DefaultFirstWarningMinutes { get; set; }
        public int DefaultSecondWarningMinutes { get; set; }
        public string UpdateUrl { get; set; }
        public bool AutoCheckUpdates { get; set; }
        public string GitHubToken { get; set; }
        
        // Sound-Einstellungen
        public bool SoundAlertsEnabled { get; set; }
        public int SoundVolume { get; set; } // 0-100
        public string FirstWarningSound { get; set; } // "beep", "bell", "alarm", "double", "chime", "pulse", "siren", "triple", "klaxon", "rising", "file_funk", "file_glocke", "file_kritisch"
        public string SecondWarningSound { get; set; }
        public int FirstWarningFrequency { get; set; } // Hz für Beep-Töne
        public int SecondWarningFrequency { get; set; }
        public bool RepeatSecondWarning { get; set; } // Wiederhole kritische Warnung
        public int RepeatWarningIntervalSeconds { get; set; }
        public List<string> QuickNoteTemplates { get; set; }

        // Zeitzone für Server-seitige Zeitstempel (IANA-ID, z.B. "Europe/Berlin")
        public string TimeZoneId { get; set; }

        // Divera 24/7 Integration
        public bool DiveraEnabled { get; set; }
        /// <summary>Staffel/Einheit API-Key — fuer Alarmabfrage (Web-API-Accesskey)</summary>
        public string DiveraAccessKey { get; set; }
        public string DiveraBaseUrl { get; set; }

        /// <summary>Poll-Intervall wenn KEIN Alarm aktiv ist (Standard: 600 = 10 Minuten)</summary>
        public int DiveraPollIntervalIdleSeconds { get; set; }

        /// <summary>Poll-Intervall wenn ein Alarm aktiv ist (Standard: 60 = 1 Minute)</summary>
        public int DiveraPollIntervalActiveSeconds { get; set; }

        public AppSettings()
        {
            Theme = "Light";
            IsDarkMode = false;
            ThemeMode = "Manual";
            DarkModeStartTime = new TimeSpan(20, 0, 0); // 20:00
            DarkModeEndTime = new TimeSpan(6, 0, 0);    // 06:00
            DefaultFirstWarningMinutes = 45;
            DefaultSecondWarningMinutes = 60;
            UpdateUrl = string.Empty;
            AutoCheckUpdates = true;
            GitHubToken = string.Empty;
            
            // Sound-Defaults
            SoundAlertsEnabled = true;
            SoundVolume = 70;
            FirstWarningSound = "beep";
            SecondWarningSound = "alarm";
            FirstWarningFrequency = 800;
            SecondWarningFrequency = 1200;
            RepeatSecondWarning = true;
            RepeatWarningIntervalSeconds = 30;
            TimeZoneId = "Europe/Berlin";
            QuickNoteTemplates = new List<string>
            {
                "ELW Ankunft Einsatzstelle",
                "ELW verlaesst Einsatzstelle",
                "Team vor Ort eingetroffen",
                "Lagemeldung an Leitstelle",
                "Suche gestartet",
                "Suche beendet"
            };

            // Divera 24/7 Defaults
            DiveraEnabled = false;
            DiveraAccessKey = string.Empty;
            DiveraBaseUrl = "https://app.divera247.com/api/v2";
            DiveraPollIntervalIdleSeconds = 600;  // 10 Minuten bei Ruhe
            DiveraPollIntervalActiveSeconds = 60; // 1 Minute bei aktivem Alarm
        }
    }
}

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
        public List<ChecklistTemplate> ChecklistTemplates { get; set; }
        public StaffelSettings StaffelSettings { get; set; }
        public AppSettings AppSettings { get; set; }

        public SessionData()
        {
            PersonalList = new List<PersonalEntry>();
            DogList = new List<DogEntry>();
            DroneList = new List<DroneEntry>();
            ChecklistTemplates = new List<ChecklistTemplate>();
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
        public string PdfLogoPfad { get; set; }

        public StaffelSettings()
        {
            StaffelName = string.Empty;
            StaffelAdresse = string.Empty;
            StaffelTelefon = string.Empty;
            StaffelEmail = string.Empty;
            StaffelLogoPfad = string.Empty;
            PdfLogoPfad = string.Empty;
        }
    }

    public class AppSettings
    {
        public string Theme { get; set; }
        public bool IsDarkMode { get; set; }
        public string ThemeMode { get; set; } // "Manual", "Auto", "Scheduled"
        public TimeSpan DarkModeStartTime { get; set; }
        public TimeSpan DarkModeEndTime { get; set; }
        public string ThemePreset { get; set; }
        public string VisualIntensity { get; set; }
        public List<CustomTheme> CustomThemes { get; set; }
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

        // Karten-Standardposition
        public double MapDefaultLat { get; set; }
        public double MapDefaultLng { get; set; }
        public int MapDefaultZoom { get; set; }

        // Pausen-Konfiguration für Hundeteams
        public int PauseThresholdMinutes { get; set; }
        public int PauseMinutesShortRun { get; set; }
        public int PauseMinutesLongRun { get; set; }

        // Monitoring für GPS-Halsbänder (aktive Suche)
        public int CollarNoSignalTimeoutSeconds { get; set; }

        // Karten-Marker Symbole
        // Collar: "paw" | "dog" | "bone" | "dot"
        // Human:  "phone" | "person" | "person_walking" | "radio" | "dot"
        public string CollarMarkerIcon { get; set; }
        public string HumanMarkerIcon { get; set; }

        /// <summary>Gibt den konfigurierten Collar-Marker-Icon-Typ zurück (nie null/leer).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string CollarMarkerIconOrDefault =>
            string.IsNullOrWhiteSpace(CollarMarkerIcon) ? "paw" : CollarMarkerIcon;

        /// <summary>Gibt den konfigurierten Human-Marker-Icon-Typ zurück (nie null/leer).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string HumanMarkerIconOrDefault =>
            string.IsNullOrWhiteSpace(HumanMarkerIcon) ? "person_walking" : HumanMarkerIcon;

        // Warnzentrum-Regelkonfiguration (keyed by WarningEntry.Source)
        public Dictionary<string, WarningRuleConfig> WarningRules { get; set; } = new();

        // Divera 24/7 Integration
        public bool DiveraEnabled { get; set; }
        /// <summary>Staffel/Einheit API-Key — fuer Alarmabfrage (Web-API-Accesskey)</summary>
        public string DiveraAccessKey { get; set; }
        public string DiveraBaseUrl { get; set; }

        /// <summary>Poll-Intervall wenn KEIN Alarm aktiv ist (Standard: 600 = 10 Minuten)</summary>
        public int DiveraPollIntervalIdleSeconds { get; set; }

        /// <summary>Poll-Intervall wenn ein Alarm aktiv ist (Standard: 60 = 1 Minute)</summary>
        public int DiveraPollIntervalActiveSeconds { get; set; }

        /// <summary>Zeitpunkt des letzten Daten-Backups (ZIP-Download). Null = noch nie.</summary>
        public DateTime? LastBackupAt { get; set; }

        /// <summary>
        /// Öffentliche Basis-URL für den Team-Mobile QR-Code (z.B. https://dein-einsatz.mywire.org).
        /// Wenn leer, wird die URL aus dem Request abgeleitet.
        /// </summary>
        public string MobileBaseUrl { get; set; } = string.Empty;

        public AppSettings()
        {
            Theme = "Light";
            IsDarkMode = false;
            ThemeMode = "Manual";
            DarkModeStartTime = new TimeSpan(20, 0, 0); // 20:00
            DarkModeEndTime = new TimeSpan(6, 0, 0);    // 06:00
            ThemePreset = ThemePresets.Nrw;
            VisualIntensity = VisualIntensityLevels.Ausgewogen;
            CustomThemes = new List<CustomTheme>();
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

            // Karten-Standardposition (Speyer als Beispiel-Default)
            MapDefaultLat = 49.3188;
            MapDefaultLng = 8.4312;
            MapDefaultZoom = 13;

            // Pausen-Defaults für Hundeteams
            PauseThresholdMinutes = 20;
            PauseMinutesShortRun = 60;
            PauseMinutesLongRun = 180;

            // Halsband-Monitoring-Defaults
            CollarNoSignalTimeoutSeconds = 30;

            // Karten-Marker-Defaults
            CollarMarkerIcon = "paw";
            HumanMarkerIcon = "person_walking";

            // Divera 24/7 Defaults
            DiveraEnabled = false;
            DiveraAccessKey = string.Empty;
            DiveraBaseUrl = "https://app.divera247.com/api/v2";
            DiveraPollIntervalIdleSeconds = 600;  // 10 Minuten bei Ruhe
            DiveraPollIntervalActiveSeconds = 60; // 1 Minute bei aktivem Alarm

            MobileBaseUrl = string.Empty;
        }
    }
}

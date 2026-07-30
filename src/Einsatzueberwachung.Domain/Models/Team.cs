// Quelle: WPF-Projekt Models/Team.cs
// Repräsentiert ein Team (Hund+Hundeführer+Helfer oder Drohnenteam) mit Timer und Warnungen
// Hinweis: Timer-Logik wird zentral durch TeamTimerService gesteuert (ein Timer für alle Teams)

using System;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Models
{
    public class Team : IDisposable
    {
        private bool _disposed = false;

        // Startzeit des Timers, gesetzt von StartTimer() und vom TeamTimerService gelesen
        public DateTime StartTime { get; private set; }

        public string TeamId { get; set; }
        public string TeamName { get; set; }
        public string DogName { get; set; }
        public string DogId { get; set; }
        public DogSpecialization DogSpecialization { get; set; }
        public string HundefuehrerName { get; set; }
        public string HundefuehrerId { get; set; }

        // Mehrere Helfer (max. 3). Index-aligned: HelferIds[i] gehört zu HelferNames[i].
        public List<string> HelferIds { get; set; } = new();
        public List<string> HelferNames { get; set; } = new();

        // Legacy-Properties für Backward-Compat mit Archiven und altem UI-Code.
        // Lesen liefert den ersten Helfer; Schreiben aktualisiert/löscht Slot 0.
        public string HelferId
        {
            get => HelferIds.Count > 0 ? HelferIds[0] : string.Empty;
            set
            {
                var v = value ?? string.Empty;
                if (string.IsNullOrEmpty(v))
                {
                    if (HelferIds.Count > 0) HelferIds.RemoveAt(0);
                    return;
                }
                if (HelferIds.Count == 0) HelferIds.Add(v);
                else HelferIds[0] = v;
            }
        }
        public string HelferName
        {
            get => HelferNames.Count > 0 ? HelferNames[0] : string.Empty;
            set
            {
                var v = value ?? string.Empty;
                if (string.IsNullOrEmpty(v))
                {
                    if (HelferNames.Count > 0) HelferNames.RemoveAt(0);
                    return;
                }
                if (HelferNames.Count == 0) HelferNames.Add(v);
                else HelferNames[0] = v;
            }
        }

        public string HelferNamesJoined => string.Join(", ", HelferNames.Where(n => !string.IsNullOrWhiteSpace(n)));
        public string SearchAreaName { get; set; }
        public string SearchAreaId { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public bool IsRunning { get; set; }
        public bool IsFirstWarning { get; set; }
        public bool IsSecondWarning { get; set; }
        public int FirstWarningMinutes { get; set; }
        public int SecondWarningMinutes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Notes { get; set; }

        public bool IsDroneTeam { get; set; }
        public string DroneType { get; set; }
        public string DroneId { get; set; }
        public bool IsSupportTeam { get; set; }

        // GPS-Halsband Zuordnung (Live-Tracking)
        public string? CollarId { get; set; }
        public string? CollarName { get; set; }

        // Gespeicherte GPS-Tracks (bei Team-Stopp gesichert)
        public List<TeamTrackSnapshot> TrackSnapshots { get; set; } = new();

        // Manuelle Kurzpause (z.B. Trinkpause) - getrennt vom Pflichtpausen-System unten.
        // Keine Mindestdauer, kein Ruhezeit-Countdown; Weiter/Start setzt sie einfach zurück.
        public bool IsManuallyPaused { get; set; }

        // Pausen-Modus für Hundeteams
        public bool IsPausing { get; set; }
        public DateTime? PauseStartTime { get; set; }
        public TimeSpan RunTimeBeforePause { get; set; }
        public int RequiredPauseMinutes { get; set; }

        // Konfigurierbare Pausen-Schwellwerte (werden von EinsatzService aus AppSettings gesetzt)
        public int PauseThresholdMinutes { get; set; } = 20;
        public int PauseMinutesShortRun { get; set; } = 60;
        public int PauseMinutesLongRun { get; set; } = 180;

        public TimeSpan PausedDuration => IsPausing && PauseStartTime.HasValue
            ? DateTime.Now - PauseStartTime.Value
            : TimeSpan.Zero;

        public int RemainingPauseMinutes => IsPausing
            ? Math.Max(0, RequiredPauseMinutes - (int)PausedDuration.TotalMinutes)
            : 0;

        public bool IsPauseComplete => IsPausing && PausedDuration.TotalMinutes >= RequiredPauseMinutes;

        private bool IsHundeteam => !IsDroneTeam && !IsSupportTeam && !string.IsNullOrWhiteSpace(DogId);

        public event Action<Team>? TimerStarted;
        public event Action<Team>? TimerStopped;
        public event Action<Team>? TimerReset;
        public event Action<Team>? TimerPaused;
        public event Action<Team, bool>? WarningTriggered;
        public event Action<Team>? TimerTick;

        public Team()
        {
            TeamId = Guid.NewGuid().ToString();
            TeamName = string.Empty;
            DogName = string.Empty;
            DogId = string.Empty;
            HundefuehrerName = string.Empty;
            HundefuehrerId = string.Empty;
            // HelferIds/HelferNames bereits durch Auto-Property-Initializer leer initialisiert
            SearchAreaName = string.Empty;
            SearchAreaId = string.Empty;
            ElapsedTime = TimeSpan.Zero;
            IsRunning = false;
            IsFirstWarning = false;
            IsSecondWarning = false;
            FirstWarningMinutes = 45;
            SecondWarningMinutes = 60;
            CreatedAt = DateTime.Now;
            Notes = string.Empty;
            IsDroneTeam = false;
            DroneType = string.Empty;
            DroneId = string.Empty;
            IsSupportTeam = false;
            IsPausing = false;
            PauseStartTime = null;
            RunTimeBeforePause = TimeSpan.Zero;
            RequiredPauseMinutes = 0;
        }

        public void StartTimer(DateTime? now = null)
        {
            if (!IsRunning)
            {
                var t = now ?? DateTime.Now;
                // Pausen-Zustand für neuen Lauf zurücksetzen
                IsPausing = false;
                IsManuallyPaused = false;
                PauseStartTime = null;
                RunTimeBeforePause = TimeSpan.Zero;
                RequiredPauseMinutes = 0;
                StartTime = t - ElapsedTime;
                IsRunning = true;
                TimerStarted?.Invoke(this);
            }
        }

        /// <summary>
        /// Manuelle Kurzpause (z.B. Trinkpause): friert die Zeit ein, ohne die Pflichtpausen-Logik auszulösen.
        /// </summary>
        public void PauseTimer(DateTime? now = null)
        {
            if (!IsRunning) return;

            var t = now ?? DateTime.Now;
            ElapsedTime = t - StartTime;
            IsRunning = false;
            IsManuallyPaused = true;
            TimerPaused?.Invoke(this);
        }

        public void StopTimer()
        {
            if (!IsRunning && !IsManuallyPaused) return;

            IsRunning = false;
            IsManuallyPaused = false;
            TimerStopped?.Invoke(this);

            if (IsHundeteam)
            {
                var pauseThreshold = PauseThresholdMinutes > 0 ? PauseThresholdMinutes : 20;
                EnterPauseMode(pauseThreshold: pauseThreshold, pauseShort: PauseMinutesShortRun, pauseLong: PauseMinutesLongRun);
            }
        }

        public void ResetTimer()
        {
            StopTimer();
            ElapsedTime = TimeSpan.Zero;
            IsFirstWarning = false;
            IsSecondWarning = false;
            IsPausing = false;
            PauseStartTime = null;
            RunTimeBeforePause = TimeSpan.Zero;
            RequiredPauseMinutes = 0;
            TimerReset?.Invoke(this);
        }

        public void EnterPauseMode(DateTime? now = null, int pauseThreshold = 20, int pauseShort = 60, int pauseLong = 180)
        {
            RunTimeBeforePause = ElapsedTime;
            RequiredPauseMinutes = ElapsedTime.TotalMinutes <= pauseThreshold ? pauseShort : pauseLong;
            PauseStartTime = now ?? DateTime.Now;
            IsPausing = true;
        }

        /// <summary>
        /// Übernimmt den Pausenstatus vom hundebezogenen Datensatz (DogPauseRecord).
        /// Wird verwendet, um Schwesterteams und neu erstellte Teams mit demselben Hund
        /// in denselben Pausenzustand zu versetzen.
        /// Es werden keine Events gefeuert – die Synchronisierung ist automatisch (kein Nutzer-Stopp).
        /// </summary>
        public void SyncPauseFromDog(DateTime pauseStartTime, TimeSpan runTimeBeforePause, int requiredPauseMinutes)
        {
            IsRunning = false;
            RunTimeBeforePause = runTimeBeforePause;
            RequiredPauseMinutes = requiredPauseMinutes;
            PauseStartTime = pauseStartTime;
            IsPausing = true;
        }

        /// <summary>
        /// Setzt den Timer zurück, ohne Events zu feuern.
        /// Wird für automatische Synchronisierung von Schwesterteams verwendet.
        /// </summary>
        public void SilentReset()
        {
            IsRunning = false;
            IsManuallyPaused = false;
            ElapsedTime = TimeSpan.Zero;
            IsFirstWarning = false;
            IsSecondWarning = false;
            IsPausing = false;
            PauseStartTime = null;
            RunTimeBeforePause = TimeSpan.Zero;
            RequiredPauseMinutes = 0;
        }

        public void CheckWarnings()
        {
            var totalMinutes = (int)ElapsedTime.TotalMinutes;

            if (!IsFirstWarning && totalMinutes >= FirstWarningMinutes)
            {
                IsFirstWarning = true;
                WarningTriggered?.Invoke(this, false);
            }

            if (!IsSecondWarning && totalMinutes >= SecondWarningMinutes)
            {
                IsSecondWarning = true;
                WarningTriggered?.Invoke(this, true);
            }
        }

        // Aufgerufen vom zentralen TeamTimerService einmal pro Sekunde
        public void Tick(DateTime now)
        {
            if (!IsRunning) return;
            ElapsedTime = now - StartTime;
            CheckWarnings();
            TimerTick?.Invoke(this);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                IsRunning = false;
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}

// Hundebezogener Pausen-Datensatz.
// Wird im EinsatzService geführt und ist unabhängig von einzelnen Team-Objekten,
// damit mehrere Teams mit demselben Hund denselben Pausenstatus sehen.

using System;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Models
{
    public class DogPauseRecord
    {
        public string DogId { get; set; } = string.Empty;
        public string DogName { get; set; } = string.Empty;
        public DateTime PauseStartTime { get; set; }
        public TimeSpan RunTimeBeforePause { get; set; }
        public int RequiredPauseMinutes { get; set; }

        public TimeSpan PausedDuration => Ortszeit.Jetzt - PauseStartTime;

        public int RemainingPauseMinutes =>
            Math.Max(0, RequiredPauseMinutes - (int)PausedDuration.TotalMinutes);

        public bool IsPauseComplete => PausedDuration.TotalMinutes >= RequiredPauseMinutes;
    }
}

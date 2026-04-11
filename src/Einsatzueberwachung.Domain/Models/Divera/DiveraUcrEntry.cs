// Divera 24/7 - User Confirm Response (Rueckmeldung eines Mitglieds auf einen Alarm)

using System;

namespace Einsatzueberwachung.Domain.Models.Divera
{
    /// <summary>
    /// Rueckmeldung eines Mitglieds auf einen Divera-Alarm (UCR = User Confirm Response)
    /// </summary>
    public class DiveraUcrEntry
    {
        /// <summary>
        /// Divera-interne User-ID des rueckmeldenden Mitglieds
        /// </summary>
        public int UserId { get; set; }

        /// <summary>
        /// Name des Mitglieds (aus data.user.items befuellt)
        /// </summary>
        public string MemberName { get; set; } = string.Empty;

        /// <summary>
        /// Rueckmeldungs-Status: 1 = Komme, 2 = Komme nicht, 3 = Vielleicht
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// Optionale Notiz zur Rueckmeldung
        /// </summary>
        public string Note { get; set; } = string.Empty;

        /// <summary>
        /// Unix-Timestamp (Sekunden) der Rueckmeldung
        /// </summary>
        public long Ts { get; set; }

        public string StatusText => Status switch
        {
            1 => "Komme",
            2 => "Komme nicht",
            3 => "Vielleicht",
            _ => "Unbekannt"
        };

        public string GetBadgeClass() => Status switch
        {
            1 => "bg-success",
            2 => "bg-danger",
            3 => "bg-warning text-dark",
            _ => "bg-secondary"
        };

        public string GetIcon() => Status switch
        {
            1 => "bi-check-circle-fill text-success",
            2 => "bi-x-circle-fill text-danger",
            3 => "bi-question-circle-fill text-warning",
            _ => "bi-dash-circle text-secondary"
        };

        public DateTime? GetTimestamp()
        {
            if (Ts <= 0) return null;
            return DateTimeOffset.FromUnixTimeSeconds(Ts).LocalDateTime;
        }
    }
}

// Divera 24/7 - Alarm-Datenmodell

using System;
using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models.Divera
{
    /// <summary>
    /// Alarm-Daten von Divera 24/7 (aus data.alarm.items)
    /// </summary>
    public class DiveraAlarm
    {
        public int Id { get; set; }

        /// <summary>
        /// Stichwort / Titel des Alarms
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Freitext / Beschreibung
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Einsatzadresse
        /// </summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>
        /// GPS-Breitengrad (optional)
        /// </summary>
        public double? Lat { get; set; }

        /// <summary>
        /// GPS-Laengsgrad (optional)
        /// </summary>
        public double? Lng { get; set; }

        /// <summary>
        /// Alarmzeit (aus Unix-Timestamp konvertiert)
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Ist der Alarm bereits geschlossen/beendet?
        /// </summary>
        public bool Closed { get; set; }

        /// <summary>
        /// Ist es ein Prioritaets-Alarm?
        /// </summary>
        public bool Priority { get; set; }

        /// <summary>
        /// Alarm-Typ / Stichwort-Kuerzel
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Externe Alarm-Nummer (z.B. Leitstellen-Einsatznummer)
        /// </summary>
        public string ForeignId { get; set; } = string.Empty;

        /// <summary>
        /// Rueckmeldungen der Mitglieder (UCR): UserId -> UcrEntry
        /// </summary>
        public List<DiveraUcrEntry> Rueckmeldungen { get; set; } = new();

        /// <summary>
        /// Zugewiesene Fahrzeug-IDs
        /// </summary>
        public List<int> AssignedVehicleIds { get; set; } = new();

        /// <summary>
        /// Anzahl positiver Rueckmeldungen (Status 1 = Komme)
        /// </summary>
        public int AnzahlKommt => Rueckmeldungen.Count(r => r.Status == 1);

        /// <summary>
        /// Anzahl negativer Rueckmeldungen (Status 2 = Komme nicht)
        /// </summary>
        public int AnzahlKommtNicht => Rueckmeldungen.Count(r => r.Status == 2);

        /// <summary>
        /// Anzahl unsicherer Rueckmeldungen (Status 3 = Vielleicht)
        /// </summary>
        public int AnzahlVielleicht => Rueckmeldungen.Count(r => r.Status == 3);
    }
}

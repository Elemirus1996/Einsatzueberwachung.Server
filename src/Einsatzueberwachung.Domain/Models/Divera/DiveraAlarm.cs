using System;
using System.Collections.Generic;
using System.Linq;

namespace Einsatzueberwachung.Domain.Models.Divera
{
    public class DiveraAlarm
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        public DateTime Date { get; set; }
        public bool Closed { get; set; }
        public bool Priority { get; set; }

        /// <summary>Key = MemberId, Value = UCR-Status (0=keine, 1=komme, 2=komme nicht, 3=spaeter)</summary>
        public Dictionary<int, int> Ucr { get; set; } = new();

        /// <summary>Aufgeloeste Rueckmeldungen mit Namen (nach PullAll befuellt)</summary>
        public List<DiveraUcrEntry> UcrDetails { get; set; } = new();

        public int CountKomme => Ucr?.Values.Count(v => v == 1) ?? 0;
        public int CountKommeNicht => Ucr?.Values.Count(v => v == 2) ?? 0;
        public int CountSpaeter => Ucr?.Values.Count(v => v == 3) ?? 0;
        public int CountKeineAntwort => Ucr?.Values.Count(v => v == 0) ?? 0;
        public string DateFormatted => Date.ToString("dd.MM.yyyy HH:mm");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Einsatzueberwachung.Domain.Models.Divera
{
    public class DiveraAlarm
    {
        public int Id { get; set; }
        /// <summary>Einsatznummer/Fremdschluessel aus Divera (z.B. "2024-001")</summary>
        public string ForeignId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        public DateTime Date { get; set; }
        public bool Closed { get; set; }
        public bool Priority { get; set; }
        /// <summary>Angaben zur meldenden Person (Melder)</summary>
        public string Caller { get; set; } = string.Empty;
        /// <summary>Bemerkungen/Hinweise fuer die Einsatzkraefte</summary>
        public string Remark { get; set; } = string.Empty;

        /// <summary>Key = MemberId, Value = StatusId (0=keine Antwort, sonst org-spezifische Status-ID)</summary>
        public Dictionary<int, int> Ucr { get; set; } = new();

        /// <summary>Aufgeloeste Rueckmeldungen mit Namen (nach PullAll befuellt)</summary>
        public List<DiveraUcrEntry> UcrDetails { get; set; } = new();

        /// <summary>Alle adressierten User-IDs aus ucr_addressed</summary>
        public List<int> AddressedUserIds { get; set; } = new();

        // Jeder nicht-null Status gilt als "hat geantwortet" (org-spezifische IDs wie 56298)
        public int CountKomme => Ucr?.Values.Count(v => v != 0) ?? 0;
        // Nur relevant wenn pull/all UCR-Daten mit Status 2 liefert (persoenlicher API-Key liefert das nicht)
        public int CountKommeNicht => Ucr?.Values.Count(v => v == 2) ?? 0;
        public int CountSpaeter => Ucr?.Values.Count(v => v == 3) ?? 0;
        public int CountKeineAntwort => Ucr?.Values.Count(v => v == 0) ?? 0;
        public string DateFormatted => Date.ToString("dd.MM.yyyy HH:mm");
    }
}

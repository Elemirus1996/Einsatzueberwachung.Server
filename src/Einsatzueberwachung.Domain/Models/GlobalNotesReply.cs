// Repräsentiert eine Antwort/Kommentar zu einem Funkspruch/Notiz-Eintrag
// Teil des Thread-Systems für erweiterte Kommunikation

using System;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Models
{
    public class GlobalNotesReply
    {
        public string Id { get; set; }
        public string NoteId { get; set; } // FK auf GlobalNotesEntry
        public string Text { get; set; }
        
        // Herkunft
        public string SourceTeamId { get; set; }
        public string SourceTeamName { get; set; }
        
        // Zeitstempel & Bearbeitung
        public DateTime Timestamp { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        
        public bool IsEdited => UpdatedAt.HasValue;

        public GlobalNotesReply()
        {
            Id = Guid.NewGuid().ToString();
            NoteId = string.Empty;
            Text = string.Empty;
            SourceTeamId = string.Empty;
            SourceTeamName = string.Empty;
            Timestamp = Ortszeit.Jetzt;
            CreatedBy = "System";
        }

        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss");
        public string FormattedDateTime => Timestamp.ToString("dd.MM.yyyy HH:mm:ss");
        public string EditedLabel => IsEdited ? $" (bearbeitet am {UpdatedAt:dd.MM.yyyy HH:mm})" : "";
    }
}

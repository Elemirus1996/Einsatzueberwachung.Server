// Optional: Historie der Bearbeitungen von Funksprüchen/Notizen
// Ermöglicht Nachvollziehbarkeit aller Änderungen

using System;
using Einsatzueberwachung.Domain.Services;

namespace Einsatzueberwachung.Domain.Models
{
    public class GlobalNotesHistory
    {
        public string Id { get; set; }
        public string NoteId { get; set; } // FK auf GlobalNotesEntry
        public string OldText { get; set; }
        public string NewText { get; set; }
        public DateTime ChangedAt { get; set; }
        public string ChangedBy { get; set; }
        public string ChangeReason { get; set; } // Optional: Grund der Änderung

        public GlobalNotesHistory()
        {
            Id = Guid.NewGuid().ToString();
            NoteId = string.Empty;
            OldText = string.Empty;
            NewText = string.Empty;
            ChangedAt = Ortszeit.Jetzt;
            ChangedBy = "System";
            ChangeReason = string.Empty;
        }

        public string FormattedChangedAt => ChangedAt.ToString("dd.MM.yyyy HH:mm:ss");
    }
}

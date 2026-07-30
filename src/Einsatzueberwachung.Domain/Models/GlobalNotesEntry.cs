// Quelle: WPF-Projekt Models/GlobalNotesEntry.cs
// Repräsentiert einen Funkspruch/Notiz-Eintrag mit Zeitstempel und Typ
// Erweitert um: Bearbeitung, Antworten/Thread, Herkunft (Team-Pflichtfeld)

using System;
using System.Collections.Generic;
using Einsatzueberwachung.Domain.Models.Enums;

namespace Einsatzueberwachung.Domain.Models
{
    public class GlobalNotesEntry
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Text { get; set; }
        public GlobalNotesEntryType Type { get; set; }
        
        // Herkunft (Team/Einheit) - PFLICHTFELD beim Erstellen
        public string SourceTeamId { get; set; }
        public string SourceTeamName { get; set; }
        public string SourceType { get; set; } // "HundeTeam", "DrohnenTeam", "Support", "Einsatzleitung"
        
        // Bearbeitungsinformationen
        public string CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public bool IsEdited => UpdatedAt.HasValue;
        
        // Antworten/Thread
        public List<GlobalNotesReply> Replies { get; set; }
        public int ReplyCount => Replies?.Count ?? 0;
        
        // Deprecated - für Rückwärtskompatibilität
        [Obsolete("Use SourceTeamId instead")]
        public string TeamId
        {
            get => SourceTeamId;
            set => SourceTeamId = value;
        }
        
        [Obsolete("Use SourceTeamName instead")]
        public string TeamName
        {
            get => SourceTeamName;
            set => SourceTeamName = value;
        }

        public GlobalNotesEntry()
        {
            Id = Guid.NewGuid().ToString();
            Timestamp = DateTime.Now;
            Text = string.Empty;
            Type = GlobalNotesEntryType.Manual;
            SourceTeamId = string.Empty;
            SourceTeamName = string.Empty;
            SourceType = "Manual";
            CreatedBy = "System";
            Replies = new List<GlobalNotesReply>();
        }

        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss");
        public string FormattedDate => Timestamp.ToString("dd.MM.yyyy");
        public string FormattedDateTime => Timestamp.ToString("dd.MM.yyyy HH:mm:ss");
        
        public string EditedLabel => IsEdited ? $" (bearbeitet am {UpdatedAt:dd.MM.yyyy HH:mm})" : "";

        public string TypeIcon
        {
            get
            {
                return Type switch
                {
                    GlobalNotesEntryType.TeamStart => "play-fill",
                    GlobalNotesEntryType.TeamStop => "stop-fill",
                    GlobalNotesEntryType.TeamPause => "pause-fill",
                    GlobalNotesEntryType.TeamReset => "arrow-repeat",
                    GlobalNotesEntryType.TeamWarning => "exclamation-triangle-fill",
                    GlobalNotesEntryType.EinsatzUpdate => "info-circle-fill",
                    GlobalNotesEntryType.System => "gear-fill",
                    _ => "chat-left-text-fill"
                };
            }
        }
        
        public string TypeColor
        {
            get
            {
                return Type switch
                {
                    GlobalNotesEntryType.TeamStart => "success",
                    GlobalNotesEntryType.TeamStop => "warning",
                    GlobalNotesEntryType.TeamPause => "info",
                    GlobalNotesEntryType.TeamReset => "info",
                    GlobalNotesEntryType.TeamWarning => "danger",
                    GlobalNotesEntryType.EinsatzUpdate => "primary",
                    GlobalNotesEntryType.System => "secondary",
                    _ => "dark"
                };
            }
        }
    }
}

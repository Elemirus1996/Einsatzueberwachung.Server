// Quelle: WPF-Projekt Models/GlobalNotesEntry.cs und NotesEntry.cs
// Beschreibt Notiz-Typen und Ziele (Global, Team-spezifisch, etc.)

namespace Einsatzueberwachung.Domain.Models.Enums
{
    public enum GlobalNotesEntryType
    {
        Manual,
        TeamStart,
        TeamStop,
        TeamPause,
        TeamReset,
        TeamWarning,
        EinsatzUpdate,
        System
    }

    public enum NoteTargetType
    {
        Global,
        Team
    }
}

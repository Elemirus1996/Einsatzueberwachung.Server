using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models;

public class EinsatzRuntimeSnapshot
{
    public EinsatzData CurrentEinsatz { get; set; } = new();
    public List<Team> Teams { get; set; } = new();
    public List<GlobalNotesEntry> GlobalNotes { get; set; } = new();
    public List<GlobalNotesHistory> NoteHistory { get; set; } = new();
}

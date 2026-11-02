using System.Collections.Generic;

namespace Einsatzueberwachung.Domain.Models;

public class EinsatzRuntimeSnapshot
{
    public EinsatzData CurrentEinsatz { get; set; } = new();
    public List<Team> Teams { get; set; } = new();
    /// <summary>Monitor-Reihenfolge als Team-IDs; ältere Snapshots haben keine gespeicherte Reihenfolge.</summary>
    public List<string> TeamOrderIds { get; set; } = new();
    public List<GlobalNotesEntry> GlobalNotes { get; set; } = new();
    public List<GlobalNotesHistory> NoteHistory { get; set; } = new();

    /// <summary>
    /// Persistierte hundebezogene Pausendatensätze. Wird beim Import gegenüber der
    /// Rekonstruktion aus Team-Zuständen bevorzugt (deckt auch abgeschlossene Pausen ab,
    /// deren IsPausing inzwischen false ist).
    /// </summary>
    public List<DogPauseRecord> DogPauses { get; set; } = new();

    /// <summary>
    /// Live-Halsband-Positionsverlauf der laufenden Suche (pro Halsband-ID).
    /// Wird persistiert damit der Hundetrack nach einem Server-Neustart vollständig bleibt.
    /// </summary>
    public Dictionary<string, List<CollarLocation>> CollarLocationHistory { get; set; } = new();

    /// <summary>
    /// Live-Telefon-GPS-Track der laufenden Suche (pro Team-ID).
    /// Wird persistiert damit der Mensch-Laufweg nach einem Server-Neustart vollständig bleibt.
    /// </summary>
    public Dictionary<string, List<TeamPhoneLocation>> PhoneTrackHistory { get; set; } = new();
}

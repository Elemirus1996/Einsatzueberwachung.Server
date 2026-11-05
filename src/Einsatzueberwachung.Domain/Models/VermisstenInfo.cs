using System;

namespace Einsatzueberwachung.Domain.Models
{
    public enum OrientierungsStatus { Unbekannt, Gut, Eingeschraenkt }
    public enum RisikoStatus { Unbekannt, Nein, Ja }
    public enum MobilitaetsStatus { Unbekannt, ZuFuss, Rollator, Rollstuhl, Fahrzeug }

    public class VermisstenInfo
    {
        // Stabile ID für Multi-Vermissten-Liste (für UI-Keys, SignalR-Diffs).
        public Guid Id { get; set; } = Guid.NewGuid();

        // Person
        public string Vorname { get; set; } = string.Empty;
        public string Nachname { get; set; } = string.Empty;
        public string Alter { get; set; } = string.Empty;
        public string Geburtsdatum { get; set; } = string.Empty;
        public string Beschreibung { get; set; } = string.Empty;
        public string Kleidung { get; set; } = string.Empty;
        public string Besonderheiten { get; set; } = string.Empty;

        // Letzter bekannter Aufenthalt
        public string ZuletztGesehenOrt { get; set; } = string.Empty;
        public string ZuletztGesehenZeit { get; set; } = string.Empty;
        public string ZuletztGesehenVon { get; set; } = string.Empty;

        // Medizin & Verhalten
        public string Vorerkrankungen { get; set; } = string.Empty;
        public string Medikamente { get; set; } = string.Empty;
        public OrientierungsStatus Orientierung { get; set; } = OrientierungsStatus.Unbekannt;
        public MobilitaetsStatus Mobilitaet { get; set; } = MobilitaetsStatus.Unbekannt;
        public RisikoStatus Suizidrisiko { get; set; } = RisikoStatus.Unbekannt;
        public RisikoStatus Bewaffnet { get; set; } = RisikoStatus.Unbekannt;

        // Legacy-Importfelder: neue Kontakte und Abstimmungen werden am Einsatz gespeichert.
        public string PolizeiKontaktName { get; set; } = string.Empty;
        public string PolizeiDienstnummer { get; set; } = string.Empty;
        public string PolizeiTelefon { get; set; } = string.Empty;
        public bool PolizeiVermisstenmeldungAufgenommen { get; set; }
        public bool PolizeiKoordinationBesprochen { get; set; }
        public bool PolizeiSuchabschnittAbgestimmt { get; set; }
        public bool PolizeiRueckmeldepflichtVereinbart { get; set; }
        public bool PolizeiDatenschutzGeklaert { get; set; }

        // Legacy-Importfelder: neue BOS-Kontakte und Abstimmungen werden am Einsatz gespeichert.
        public string BosEinheit { get; set; } = string.Empty;
        public string BosZugfuehrer { get; set; } = string.Empty;
        public string BosFunkrufname { get; set; } = string.Empty;
        public string BosAufgabenteilung { get; set; } = string.Empty;
        public bool BosAbschnittAbgestimmt { get; set; }
        public bool BosRessourcenBesprochen { get; set; }

        // Szenarioabhängige Checkliste (z.B. Gebäudeteil bei Trümmer, Geruchsstück bei Mantrailer)
        public ChecklistInstance? Checkliste { get; set; }

        // Zeitstempel der letzten Änderung
        public DateTime? ZuletztAktualisiert { get; set; }

        public string VollerName => string.IsNullOrWhiteSpace(Vorname) && string.IsNullOrWhiteSpace(Nachname)
            ? "Unbekannt"
            : $"{Vorname} {Nachname}".Trim();
    }
}

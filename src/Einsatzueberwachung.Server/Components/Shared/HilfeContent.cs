namespace Einsatzueberwachung.Server.Components.Shared;

public record HilfeEntry(string Titel, string Beschreibung, string[] Klickpfade);

public static class HilfeContent
{
    public static readonly Dictionary<string, HilfeEntry> Eintraege = new()
    {
        ["/"] = new(
            "Home / Dashboard",
            "Das Dashboard ist die Startseite der Anwendung. Hier werden aktive Einsätze, offene Warnungen und schnelle Navigationsmöglichkeiten angezeigt. Globale Notizen können direkt auf dieser Seite eingesehen und bearbeitet werden. Der aktuelle Einsatzstatus wird oben in der Titelleiste angezeigt.",
            new[]
            {
                "Navigation (links) -> Home",
                "Globale Notizen: Direkt auf der Seite sichtbar und editierbar"
            }),

        ["/einsatz-start"] = new(
            "Neuer Einsatz",
            "Hier wird ein neuer Einsatz angelegt. Einsatzort, Szenario (Mantrailer, Fläche, Trümmer, Sonstige) und erste Vermissteninformationen werden eingetragen. Das gewählte Szenario steuert, welche Features in der App aktiv sind. Ein laufender Einsatz muss zuerst beendet werden, bevor ein neuer angelegt werden kann.",
            new[]
            {
                "Navigation -> Neuer Einsatz",
                "Szenario wählen -> Einsatzort eintragen -> Vermisste eintragen -> 'Einsatz starten'"
            }),

        ["/einsatz-monitor"] = new(
            "Einsatz-Monitor",
            "Die zentrale Übersicht aller Teams und eingecheckten Personen und Hunde im laufenden Einsatz. Das Ressourcenpanel sortiert sofort verfügbare Kräfte nach oben. Teams können gestartet, pausiert und zurückgemeldet werden. Neue Teams lassen sich direkt hinzufügen und bestehende bearbeiten oder entfernen.",
            new[]
            {
                "Navigation -> Monitor",
                "Team starten: Team-Karte -> 'Start'-Button",
                "Team pausieren: Team-Karte -> 'Pause'-Button",
                "Team zurückmelden: Team-Karte -> 'Zurück'-Button",
                "Neues Team: '+ Team hinzufügen'-Button oben rechts",
                "Ressourcen verwalten: Panel-Link 'Ressourcenverwaltung'"
            }),

        ["/einsatz-karte"] = new(
            "Einsatz-Karte",
            "Interaktive Leaflet-Karte mit GPS-Tracking aller Teams, Suchgebieten und Kartenpunkten. Die Karte zeigt Live-Positionen der Teams (Handy-Tracking) und Halsbandsender. Suchgebiete können als Polygone eingezeichnet werden. Über die Tabs wechselt man zwischen GPS-Ansicht, Gebieten und Punkten.",
            new[]
            {
                "Navigation -> Karte",
                "Suchgebiet einzeichnen: Tab 'Gebiete' -> 'Gebiet zeichnen'",
                "Kartenpunkt setzen: Tab 'Punkte' -> Karte anklicken",
                "Punkt als Navigationsziel an ein Team: Tab 'Punkte' -> Senden-Symbol am Punkt -> Team wählen",
                "Orange Punkte hat ein Team in der App gesetzt (z. B. Fund); sie erscheinen sofort, dazu eine Notiz",
                "GPS-Track ansehen: Tab 'GPS'"
            }),

        ["/truemmer"] = new(
            "Trümmer-Lagekarte",
            "Pixel-basierte Lagekarte für Trümmer-Szenarien. Ein Drohnenfoto wird als Hintergrund hochgeladen, Suchbereiche werden als Polygone direkt auf dem Bild eingezeichnet. Diese Seite ist nur aktiv, wenn das Szenario 'Trümmer' gewählt wurde. Mehrere Karten pro Einsatz sind möglich.",
            new[]
            {
                "Nur sichtbar bei Szenario 'Trümmer'",
                "Navigation -> Trümmer",
                "Karte hochladen: 'Neue Karte' -> Drohnenfoto auswählen (JPG/PNG/WEBP, max. 20 MB)",
                "Bereich einzeichnen: Karte öffnen -> 'Bereich zeichnen' -> Polygon auf Bild klicken"
            }),

        ["/einsatz-archiv"] = new(
            "Einsatz-Archiv",
            "Übersicht aller abgeschlossenen Einsätze. Jeder Einsatz kann geöffnet, als PDF-Bericht exportiert oder als Backup-Paket heruntergeladen werden. Das Archiv erlaubt auch den Import früherer Einsätze zur Nachbereitung.",
            new[]
            {
                "Navigation -> Archiv",
                "Einsatz öffnen: Zeile anklicken",
                "PDF exportieren: Einsatz öffnen -> 'PDF exportieren'",
                "Backup herunterladen: Einsatz öffnen -> 'Backup herunterladen'"
            }),

        ["/einsatz-leitung"] = new(
            "Einsatzleitung",
            "Detailansicht des laufenden Einsatzes mit erweiterter Steuerung. Hier können Vermisste verwaltet, Checklisten bearbeitet und EL-Notizen erfasst werden. Auch der Einsatz kann von hier aus beendet werden.",
            new[]
            {
                "Navigation -> Monitor -> 'Zur Einsatzleitung'",
                "Vermisste hinzufügen: Abschnitt 'Vermisste' -> '+ Vermisster'",
                "Checkliste öffnen: Vermissten-Karte -> Checklisten-Icon",
                "Einsatz beenden: 'Einsatz beenden'-Button"
            }),

        ["/wetter"] = new(
            "Wetter",
            "Zeigt aktuelle Wetterdaten und Vorhersagen vom Deutschen Wetterdienst (DWD) für den Einsatzort. Auch Flugwetter-Informationen für Drohneneinsätze sind verfügbar. Die Daten werden automatisch aktualisiert.",
            new[]
            {
                "Navigation -> Wetter",
                "Einsatzort muss in den Einstellungen oder beim Einsatz-Start hinterlegt sein"
            }),

        ["/divera"] = new(
            "Divera 24/7",
            "Integration mit dem Alarmierungssystem Divera 24/7. Zeigt aktuelle Alarmierungen, Mitgliederstatus und UCR-Rückmeldungen. Positive Rückmeldungen zum aktiven DIVERA-Alarm heben die eindeutig zugeordneten Personen im Check-in der Ressourcenverwaltung hervor; dort können sie einzeln oder gesammelt mit Status „Auf Anfahrt“ übernommen werden. Hunde werden nicht automatisch eingecheckt. Dafür muss die Divera Benutzer-ID bei der Person in den Stammdaten hinterlegt sein. Zum gefahrlosen Testen ohne Verbindung zum echten DIVERA-System steht in den Einstellungen ein lokaler Testalarm bereit.",
            new[]
            {
                "Navigation -> Divera 24/7",
                "API-Key konfigurieren: Einstellungen -> Integration -> Divera 24/7",
                "Lokalen Testalarm starten: Einstellungen -> Integration -> 'Testalarm starten'",
                "Divera Benutzer-ID pflegen: Stammdaten -> Personal",
                "DIVERA-Zusagen übernehmen: Navigation -> Ressourcenverwaltung -> 'Alle aus DIVERA einchecken'"
            }),

        ["/ressourcenverwaltung"] = new(
            "Ressourcenverwaltung",
            "Hier werden Personen und ihre Hunde für den laufenden Einsatz eingecheckt und disponiert. Im Check-in werden aktive, noch nicht eingecheckte Personen angeboten; positive Rückmeldungen aus dem verknüpften DIVERA-Alarm werden oben hervorgehoben und können einzeln oder über „Alle aus DIVERA einchecken“ gesammelt als „Auf Anfahrt“ übernommen werden. Zugeordnete Hunde lassen sich direkt am Eintrag der eingecheckten Person hinzufügen und behalten ihren eigenständigen Status. Fehlende Personen werden per Schnellaufnahme dauerhaft in den Stammdaten gespeichert und eingecheckt. „In Suche“ kann automatisch durch einen gestarteten Suchauftrag oder manuell gesetzt werden. Eine manuelle Statusänderung erzeugt keine Sektorzuweisung; nur Ressourcen eines Suchauftrags werden nach dessen Abschluss automatisch zurückgesetzt. Für einen Sonderauftrag ist ein Zweck erforderlich. Nur Ressourcen mit Status „Im Verfügungsraum“ sind für neue Suchsektoren auswählbar.",
            new[]
            {
                "Navigation -> Ressourcenverwaltung",
                "Check-in: Person mit 'Einchecken' übernehmen oder 'Alle aus DIVERA einchecken' nutzen -> zugeordnete Hunde direkt an der Person hinzufügen",
                "Fehlende Person: 'Schnell erfassen' -> in Stammdaten speichern und einchecken",
                "Sonderauftrag: Status wählen -> Zweck eintragen -> bestätigen",
                "DIVERA-Zuordnung für Personen: Stammdaten -> Personal -> Divera Benutzer-ID"
            }),

        ["/stammdaten"] = new(
            "Stammdaten",
            "Verwaltung aller Stammdaten: Hunde, Personen, Drohnen und Checklisten-Templates. Einträge können angelegt, bearbeitet und gelöscht werden. Checklisten-Templates werden pro Szenario hinterlegt und beim Anlegen eines Vermissten automatisch angehängt.",
            new[]
            {
                "Navigation -> Stammdaten",
                "Tab wählen: Hunde / Personen / Drohnen / Checklisten",
                "Neuer Eintrag: '+ Hinzufügen'-Button",
                "Checklisten-Template bearbeiten: Tab 'Checklisten' -> Template anklicken"
            }),

        ["/audit-log"] = new(
            "Audit-Log",
            "Chronologisches Protokoll aller sicherheitsrelevanten Aktionen in der Anwendung. Zeigt, wer wann welche Änderungen vorgenommen hat. Dient der Nachvollziehbarkeit bei der Einsatznachbereitung.",
            new[]
            {
                "Navigation -> Audit-Log",
                "Filter: Zeitraum oder Aktion eingeben"
            }),

        ["/warnzentrum"] = new(
            "Warnzentrum",
            "Zentrale Übersicht aller aktiven und vergangenen Warnungen. Warnungen entstehen automatisch bei Überschreitung von Team-Timern oder konfigurierten Schwellwerten. Warnregeln können hier konfiguriert werden.",
            new[]
            {
                "Navigation -> Warnzentrum",
                "Warnregel anpassen: Abschnitt 'Regeln' -> Regel anklicken"
            }),

        ["/einstellungen"] = new(
            "Einstellungen",
            "Konfiguration der Anwendung: Staffel-Stammdaten, Einsatzbetrieb, Erscheinungsbild (Theme), Tastenkürzel, Divera-Integration und Systemwartung. Unter 'Integration' lässt sich außerdem ein rein lokaler DIVERA-Testalarm mit Beispiel-Rückmeldungen starten, ohne eine echte Alarmierung auszulösen. Im Tab 'Tastenkürzel' lassen sich alle Navigationskürzel und der Stepper auf 'Neuer Einsatz' frei belegen. Kürzel werden per Klick auf das Eingabefeld und dann Drücken der gewünschten Taste aufgezeichnet.",
            new[]
            {
                "Navigation -> Einstellungen",
                "DIVERA lokal testen: Tab 'Integration' -> 'Testalarm starten' / 'Testalarm beenden'",
                "Tab auswählen (z.B. Tastenkürzel) -> Eingabefeld anklicken -> Taste drücken -> wird sofort gespeichert",
                "Tastenkürzel zurücksetzen: Reset-Pfeil-Schaltfläche neben dem Feld oder 'Alle zurücksetzen'"
            }),

        ["/einsatz-import-export"] = new(
            "Import / Export",
            "Export des laufenden Einsatzes als Backup-Paket (.zip) sowie Import von Einsatz-Paketen anderer Geräte. Unterstützt auch den Merge-Workflow: Zwei Einsätze vom selben Vorfall können zusammengeführt werden.",
            new[]
            {
                "Navigation -> Import / Export",
                "Export: 'Einsatz exportieren' -> ZIP herunterladen",
                "Import: 'Einsatz importieren' -> ZIP-Datei auswählen",
                "Merge: Nach Import mit gleichem Einsatz -> 'Zusammenführen'"
            }),
    };
}

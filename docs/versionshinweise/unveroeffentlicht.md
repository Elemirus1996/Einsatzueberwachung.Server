## Neu

### Neuer Einsatzbericht
- Der Bericht beginnt mit einer **Übersichtsseite für Polizei und Behörde**: Einsatznummer, Vorgangsnummer der Polizei, Datum und Art, das Ergebnis mit Fundzeit, Fundort (WGS84 und UTM), „Gefunden durch“ und Übergabe, dazu Polizeikontakt, vermisste Person, zeitlicher Ablauf, Suche je Team, Trails, eingesetzte Kräfte und ein Feld für die Unterschrift der Einsatzleitung.
- **Folgeseiten:** Übersichtskarte aller Suchgebiete und Tracks mit Legende in den Teamfarben, eine Karte je Trail mit Start und Ende der Spur, Track-Tabelle, Teams, Vor Ort sowie Funk und Notizen (ohne Timer-, System- und Testeinträge). Alle Karten haben Nordpfeil und Maßstab.
- **Flächenabdeckung steht nicht mehr im Bericht.** Im Monitor bleibt sie wie bisher.
- Beim Beenden lassen sich **Abschnitte des Berichts an- und abwählen** (Vermisste, Zeitlicher Ablauf, Suche je Team, Karten, Funk und Notizen, Vor Ort). Kopf, Ergebnis und Polizei stehen immer drin.

### „Eingesetzt als“ je Team
- Im Teamdialog des Monitors wählt ihr, als was ein Team eingesetzt ist: Fläche, Trümmer, Mantrailer, Wasser oder Lawine. Zur Auswahl stehen die Spezialisierungen des Hundes laut Stammdaten; hat er genau eine, ist sie vorausgewählt.
- Die Einsatzart wird beim Start jeder Suche festgehalten. So weiß der Bericht für jede Suche, ob es ein Trail oder eine Flächensuche war.

### „Alarmiert durch“ ist jetzt Pflicht
- Beim Anlegen eines Einsatzes wählt ihr, wer alarmiert hat: Polizei, Feuerwehr, Rettungsdienst oder andere Rettungshundestaffel. Stelle oder Name könnt ihr zusätzlich eintragen. Beim Start aus Divera wird der Melder übernommen, die Auswahl trefft ihr selbst.

### Beenden fragt alle offenen Angaben ab
- Der Dialog „Einsatz beenden“ zeigt einen Block „Angaben für den Bericht“: Alarmiert durch, Vorgangsnummer und Polizeikontakt (nur wenn die Polizei alarmiert hat, nie bei Übungen), bei einem Fund Fundzeit, Fundort, „Gefunden durch“ und Übergabe, „Eingesetzt als“ je Team und für jeden Trail Abbruchgrund und Geruchsartikel.
- Fehlt etwas, nennt der Dialog es. Beenden geht trotzdem, aber nur mit dem Haken „Trotzdem ohne diese Angaben beenden“. **Nach dem Beenden ist nichts mehr änderbar** – deshalb jetzt die Nachfrage, gerade wenn die Polizei die Vorgangsnummer erst spät nennt.
- „Keine Polizei beteiligt“ ankreuzen, wenn das zutrifft: Dann entfallen die Polizeiangaben, und der Bericht nennt es so.
- Die **Abbruchgründe für Trails** stehen unter Einstellungen → Einsatzbetrieb und lassen sich dort anpassen.

### Fund-Punkt aus der App
- Teams können in der App einen Punkt als **Fund** setzen. Er erscheint im Monitor rot mit Warnhinweis und wird beim Beenden als Fundort vorgeschlagen („Fund-Punkt übernehmen“). Dafür braucht es die neue App-Version.

### Einstellungen
- Datenschutzerklärung für die App, Packliste und Löschfristen stehen jetzt unter Einstellungen → **Staffel**.
- Unter Einstellungen → System → Updates stehen die **Versionshinweise** der neuesten Version – bei einem verfügbaren Update vor dem Installieren, sonst für die installierte Version.

## Technik
- Schnittstelle erweitert (Beschreibung in `docs/APP_API.md`, Einsatzleitung Abschnitt 10): `einsatzart` in `/lage` und `/tracks`, `typ` bei `POST /punkt`, `alarmiertDurch` und `melder` bei `/einsatz/start`, neu `GET /einsatz/abschluss`, bei `/einsatz/beenden` die Felder `abschluss`, `unvollstaendigBestaetigt` und `pdf.abschnitte`, bei `/einsatz/bericht` der Parameter `abschnitte`. Ältere Apps laufen weiter wie bisher; die neuen Angaben werden dann im Monitor beim Beenden abgefragt.
- Keine neuen Pakete, keine nginx-Änderung, keine Datenmigration. Neue Einstellungen werden beim ersten Speichern angelegt.

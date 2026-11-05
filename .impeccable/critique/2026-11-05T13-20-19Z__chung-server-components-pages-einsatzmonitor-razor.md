---
target: EinsatzMonitor
total_score: 27
p0_count: 0
p1_count: 2
audit_score: 14
audit_total: 20
timestamp: 2026-11-05T13-20-19Z
slug: chung-server-components-pages-einsatzmonitor-razor
---
# Einsatzmonitor — Designkritik und technisches Audit

Prüfstand: 2026-11-04. Es wurden keine UI- oder Quellcodeänderungen vorgenommen.

## Executive Summary

- **Audit Health Score: 14/20 — Good.** Die technische Basis ist responsiv und tokenisiert; die wichtigsten offenen Punkte liegen bei zugänglichen Namen, Touch-Größen und der Dichte auf kleinen Ansichten.
- **Nielsen-Kritik: 27/40 — Acceptable.** Der Einsatzmonitor wirkt fachlich passend und bedienbar, aber im Scan konkurrieren zu viele Flächen und Bedienelemente um Aufmerksamkeit.
- **Befunde:** 0 P0, 2 P1, 3 P2, 1 P3.
- **Anti-Patterns-Verdict:** Kein AI-Slop-Gallery. Die Oberfläche ist erkennbar für Einsatzbetrieb gestaltet. Die Irritation entsteht eher durch lokale Stil- und Dichteunterschiede als durch eine falsche Palette: verschachtelte Flächen, viele kleine Aktionen, ein auffälliger Leuchteffekt am Plus-Button und einzelne Detector-Treffer.
- **Trend:** 30/40 → 34/40 → 27/40 (−7 zum letzten Snapshot). Der aktuelle Durchgang ergänzt die früheren visuellen Reviews um Live-Prüfungen bei Tablet- und Mobilbreiten, Touch-Zielgrößen und zugänglichen Namen. Daher ist das eine qualitative Momentaufnahme, kein belegter Code-Regressionstest.

## Audit Health Score

| # | Dimension | Score | Wichtigster Befund |
|---|-----------|------:|--------------------|
| 1 | Accessibility | 2/4 | Zwei sichtbare Bedienelemente ohne zugänglichen Namen; mehrere Touch-Ziele unter 44 px |
| 2 | Performance | 3/4 | Layout-Animationen für Breite, Höhe, Padding und Margin erkannt; kein sichtbarer Jank belegt |
| 3 | Responsive Design | 3/4 | Kein horizontaler Overflow bei 390, 767, 768, 894, 1200 und 1201 px; Team-Überschrift bei 390 px abgeschnitten |
| 4 | Theming | 3/4 | Token-System und Dark Mode vorhanden; einzelne harte Farben und ein Gradient-Treffer bleiben |
| 5 | Anti-Patterns | 3/4 | Insgesamt eigenständige Einsatzoberfläche; einige Stil- und Browser-Detector-Treffer sind zu verifizieren |
| **Total** | | **14/20** | **Good — gezielte Verbesserungen, kein grundlegender Umbau nötig** |

### Nielsen-Heuristiken

| Heuristik | Score | Begründung |
|-----------|------:|------------|
| Sichtbarkeit des Systemstatus | 3/4 | Laufend/Bereit, Einsatzdauer, Uhr und Warnungen sind sichtbar; ein einziger priorisierter „Jetzt handeln“-Fokus fehlt. |
| Übereinstimmung mit der realen Welt | 4/4 | Einsatz, Teams, Suchgebiete, Funk und Notizen entsprechen der Fachsprache. |
| Nutzerkontrolle und Freiheit | 3/4 | Panels und Aktionen sind erreichbar; viele davon sind kompakt oder icon-only und bieten nicht überall eine sichtbare Rücknahme. |
| Konsistenz und Standards | 3/4 | Tokens und Standard-Controls werden genutzt; Module kombinieren dennoch unterschiedliche Abstände, Flächen und Button-Stile. |
| Fehlervermeidung | 2/4 | In den Teamkarten liegen operative Aktionen dicht bei Metadaten; die Konsequenz und Rückmeldung ist im Ruhezustand nicht immer erkennbar. |
| Erkennen statt Erinnern | 3/4 | Statuswörter und Zähler helfen; Icon-Aktionen und knappe Beschriftungen verlangen Kontextwissen. |
| Flexibilität und Effizienz | 3/4 | Tablet-Tabs, Popouts und Shortcuts sind gute Grundlagen; Panelwechsel und interne Listen-Scrolls kosten beim Vergleichen zusätzliche Schritte. |
| Ästhetik und Minimalismus | 2/4 | Die Oberfläche ist fachlich informativ, aber mehrere ähnlich gewichtete Bereiche und verschachtelte Flächen konkurrieren. |
| Fehlererkennung und -behebung | 2/4 | Statusfeedback ist vorhanden, aber eine übergreifende Sicht auf Änderungen und eine überall klare Rückkehr fehlen. |
| Hilfe und Dokumentation | 2/4 | Hilfe ist erreichbar, aber überwiegend verborgen statt direkt am Entscheidungspunkt. |
| **Gesamt** | **27/40** | **Acceptable — die Struktur funktioniert, die Scan-Hierarchie braucht eine fokussierte Runde.** |

## Befunde nach Priorität

### [P1] Keine eindeutige Erstpriorität im Blick

- **Ort:** Dashboard-Zusammenfassung und Panel-Komposition in `EinsatzMonitor.razor`; Tablet-Anordnung in `monitor.css`.
- **Kategorie:** Visuelle Hierarchie / kognitive Last.
- **Auswirkung:** Beim schnellen Blick sieht man mehrere gleichzeitige Einstiegspunkte — Lageinfos, Tabs, Teamliste, Filter, Popout und Hinzufügen. Es fehlt ein klares visuelles Signal, welches Element als Nächstes Aufmerksamkeit braucht. Das erhöht die Suchzeit gerade dann, wenn mehrere Statusmeldungen eintreffen.
- **Standard:** Nielsen #1 und #8; kein einzelner WCAG-Verstoß.
- **Empfehlung:** Nicht mehr Farbe hinzufügen. Eine dominante Prioritätsstufe festlegen: zuerst aktive Warnung/Handlungsbedarf, dann Teams, danach Kontext. Leuchteffekt und Nebenaktionen dürfen diese Reihenfolge nicht brechen.
- **Vorgeschlagener Befehl:** `/impeccable layout`

### [P1] Zugängliche Namen und Feld-Touchziele sind nicht durchgängig

- **Ort:** Team-Panel und Notizformular in `EinsatzMonitor.razor`; Verschiebe- und Bearbeitungsaktionen in Team-/Suchgebietsmodulen; Touch-Regel in `DESIGN.md`.
- **Kategorie:** Accessibility / Responsive.
- **Auswirkung:** Bei 390 px wurden zwei sichtbare Elemente ohne zugänglichen Namen erkannt: ein Plus-Button ohne Text/ARIA-Name und das Notiz-Textarea mit nur einem Placeholder. Zusätzlich messen Verschiebe-Handles etwa 18×18 px und Bearbeitungsbuttons etwa 32×32 px. Das erschwert Screenreader- und Touch-Bedienung.
- **WCAG/Standard:** WCAG 4.1.2 (Name, Rolle, Wert) und 3.3.2 (Labels/Instructions); WCAG 2.5.8 ist bei den kleinsten Zielen zu prüfen, sofern keine Ausnahme durch Abstand greift. Das Projekt-Designsystem fordert 44×44 px für Touch-Ziele. Die Tastatur-Fokusmarkierung selbst war sichtbar: 2 px blau am Menü-Button.
- **Empfehlung:** Plus-Aktion explizit benennen, das Notizfeld mit einem persistenten Label versehen und alle häufigen Touch-Aktionen auf mindestens 44×44 px vergrößern. Sichtbaren Fokus beibehalten.
- **Vorgeschlagener Befehl:** `/impeccable harden`

### [P2] Team-Panel wird auf Mobilbreite zu eng

- **Ort:** Team-Panel-Header und responsive Regeln in `EinsatzMonitor.razor` und `monitor.css`.
- **Kategorie:** Responsive.
- **Auswirkung:** Bei 390 px wird die Überschrift „Teams“ visuell zu „T…“ gekürzt, während Filter und mehrere Icon-Aktionen Platz beanspruchen. Gleichzeitig ist die Teamliste intern scrollbar, während die Seite selbst ebenfalls scrollt. Es gab bei den geprüften Breiten keinen horizontalen Dokument-Overflow; das Problem ist die enge Hierarchie, nicht ein grundsätzlich gebrochenes Layout.
- **Standard:** Projektregel Touch-Ziele ≥44 px; keine bestätigte WCAG-Overflow-Verletzung.
- **Empfehlung:** Headerbeschriftung schützen, sekundäre Aktionen auf kleinen Ansichten bündeln und den aktiven Arbeitsbereich nicht in konkurrierende Scrollcontainer zerlegen.
- **Vorgeschlagener Befehl:** `/impeccable adapt`

### [P2] Mehrere visuelle Grammatiken überlagern sich

- **Ort:** Zusammenfassung, Tabs, Team-Panel und Teamkarten; visuelle Leitplanken in `DESIGN.md` und `theme-tokens.css`.
- **Kategorie:** Konsistenz / Anti-Pattern.
- **Auswirkung:** Dunkle Flächen, Rahmen und Rundungen sind grundsätzlich passend. Durch mehrere verschachtelte Oberflächen, wechselnde Überschriftengrößen und den leuchtenden Plus-Button entsteht aber ein „zusammengebautes“ Gefühl. Der Plus-Button zieht ähnlich viel Aufmerksamkeit wie operative Statushinweise, obwohl er eine Nebenaktion ist.
- **Standard:** Projektprinzip „leise per Default“ und konsistente Typografie/Tiefe.
- **Empfehlung:** Ein Flächen- und Abstandsmodell pro Ebene festlegen; Glow/Gradient nur für echte Eskalationen reservieren; Überschriften, sekundäre Labels und Statuswerte mit wenigen klaren Größenstufen vereinheitlichen.
- **Vorgeschlagener Befehl:** `/impeccable quieter`

### [P2] Layout-Animationen und mögliche Overflow-Clips

- **Ort:** `layout.css` bei den Übergängen für Breite und `monitor.css` bei einem 3-px-Top-Rahmen.
- **Kategorie:** Performance / Interaktion.
- **Auswirkung:** Der Quell-Detector fand Übergänge für `width`, `max-height`, `padding` und `margin`; diese Eigenschaften lösen Layout-Arbeit aus. Der Browser-Detector markierte außerdem positionierte Kinder, die von `.page.sidebar-collapsed` und `main` geclippt werden. Ein konkreter, für Nutzer reproduzierbarer Menü- oder Tooltip-Clip wurde in diesem Durchgang nicht verifiziert.
- **Standard:** Vermeidung unnötiger Layout-Animationen; Overflow-Verhalten von Menüs/Tooltips prüfen.
- **Empfehlung:** Übergänge auf `transform`/`opacity` beschränken, wo das Bewegungsziel gleich bleibt; die markierten Popover-/Tooltip-Konstellationen gezielt auf Clipping testen.
- **Vorgeschlagener Befehl:** `/impeccable optimize`

### [P3] Einzelne Detector-Treffer brauchen Kontextprüfung

- **Ort:** `monitor.css` (3-px-Top-Rahmen) und Hilfs-/Überschriftentexte im gerenderten Monitor.
- **Kategorie:** Anti-Pattern / Accessibility.
- **Auswirkung:** Der Quellscan meldet einen Akzent-Rahmen auf einem gerundeten Element. Der Browser-Scan meldet zusätzlich Gradient-Text, eine H2→H5-Überschriftenlücke und den Ausdruck „X theater“. Diese Treffer waren nicht alle als sichtbare Probleme im Standardzustand reproduzierbar und dürfen nicht pauschal als Designfehler behandelt werden.
- **Standard:** Re-Derivation-Rule; semantische Überschriftenhierarchie.
- **Empfehlung:** Nur die tatsächlich sichtbaren, absichtslosen Fälle entfernen; falls die Hilfe geöffnet wird, Überschriftenfolge und verständliche Formulierung dort erneut prüfen.
- **Vorgeschlagener Befehl:** `/impeccable audit`

## Systemische Muster

- Die Design-Tokens sind vorhanden; das unstimmige Gefühl entsteht eher durch modulare Dichte und wechselnde Mikroregeln als durch eine grundsätzlich falsche Palette.
- In der mobilen Ansicht konzentrieren sich viele schmale Targets und Icon-Aktionen im selben Header. Zugängliche Namen und Touch-Flächen sollten komponentenübergreifend geprüft werden.
- Tablet-Tabs reduzieren vertikales Stapeln sinnvoll. Die weiterhin internen Scrollcontainer erschweren jedoch den schnellen Vergleich zwischen Teams und Kontext.
- Der Einsatzmonitor trägt bereits Statuswörter und weitere Kanäle neben Farbe. Das sollte bei Warnungen und Teamzuständen konsequent beibehalten werden.

## Persona-Prüfung

### Alex — ungeduldiger Power-User

- **Rote Flaggen:** Kein klarer „Jetzt handeln“-Einstieg; der Blick muss zwischen Zusammenfassung, Teams, Notizen und Suchgebieten wechseln. Mehrere kleine Header-Aktionen konkurrieren mit dem häufigen Team-Scan.
- **Was gut funktioniert:** Tablet-Tabs, Popouts und Tastatur-Shortcuts sind gute Beschleuniger; die Mission-Topbar bleibt bei etwa 1110 px einzeilig.

### Sam — Screenreader-/Tastatur-Nutzer

- **Rote Flaggen:** Plus-Button ohne zugänglichen Namen, nur per Placeholder benanntes Notizfeld und sehr kleine Verschiebe-Handles. Statusfarbe allein ist nicht das einzige Signal; die Wörter „Bereit“ und „Laufend“ sind hilfreich.
- **Was gut funktioniert:** Der Tastatur-Fokus am Menü-Button ist sichtbar und ausreichend deutlich.

### Casey — abgelenkter mobiler Nutzer

- **Rote Flaggen:** Die Lagezusammenfassung belegt einen großen Teil des 390-px-Displays, bevor Teamaktionen beginnen; der Team-Titel wird im kompakten Header abgeschnitten. Mehrere Ziele unterschreiten 44 px.
- **Was gut funktioniert:** Kein horizontaler Overflow bei 390 px; Status und Mission bleiben verständlich.

## Kognitive Last und Nutzergefühl

- **Ankunft:** Fachsprache und sichtbarer Einsatzstatus vermitteln Orientierung.
- **Scannen:** Die Orientierung nimmt ab, sobald Zusammenfassung, Tabs, Filter, Icons, Teamkarten und Notizbereich gleichzeitig konkurrieren.
- **Handeln:** Statuswörter helfen; icon-only Aktionen und kleine Trefferflächen verlangen trotzdem Präzision.
- **Ausnahmefall:** Warnungen, Notizen und Teams liegen in getrennten Bereichen statt in einer priorisierten Handlungsfolge.

Leitfragen für die nächste Runde:

- Wenn die Einsatzleitung 30 Sekunden wegsieht: Welches eine Signal zeigt danach den wichtigsten Handlungsbedarf?
- Ist der Monitor für schnellen Vergleich zwischen Teams gestaltet oder für einzelne Detailkarten?
- Welche Aktion darf auf Touch auf keinen Fall versehentlich ausgelöst werden — und wie deutlich ist sie von passiven Informationen getrennt?

## Positive Befunde

- Eigenständige Einsatzsprache und glaubwürdige Fachstruktur statt generischer Admin-Vorlage.
- Tokenisiertes Dark-Mode-System und ruhige Grundflächen passen zum Einsatzkontext.
- Tablet-Tabs halten Teams, Notizen/Funk und Suchgebiete erreichbar, ohne die Mission-Zusammenfassung zu verlieren.
- Bei 390, 767, 768, 894, 1200 und 1201 px wurde kein horizontaler Dokument-Overflow gemessen.
- Tastaturfokus ist sichtbar; viele Statuswerte haben neben Farbe zusätzliche Labels oder Symbole.

## Prüfgrenzen

- Der Aufruf des gebündelten `detect.mjs --json` scheiterte an einer fehlenden lokalen Skill-Datei (`scripts/lib/impeccable-config.mjs`). Als Fallback lief der Regex-Engine direkt; der Browser-Detector wurde erfolgreich injiziert und zeigte ein Overlay.
- Der Kontrast-Detector konnte ein sichtbares Textbeispiel wegen eines Opacity-Stacks nicht auflösen. Es wurde deshalb kein bestätigter AA-Kontrastfehler behauptet.
- Die Browserprüfung war read-only; operative Start-/Stop-Aktionen wurden nicht ausgelöst.

## Empfohlene Reihenfolge

1. **[P1] `/impeccable layout`** — eine klare Handlungspriorität für den Monitor und seine Team-/Warnungsflächen festlegen.
2. **[P1] `/impeccable harden`** — Namen, Labels, Fokus und Touch-Zielgrößen vervollständigen.
3. **[P2] `/impeccable adapt`** — Team-Header und Scroll-Verhalten bei 390–1200 px verbessern.
4. **[P2] `/impeccable quieter`** — Glow, Rahmen und Typografie auf ein ruhigeres gemeinsames System reduzieren.
5. **[P2] `/impeccable optimize`** — Layout-Übergänge und markierte Overflow-Fälle gezielt bereinigen.
6. **[P3] `/impeccable polish`** — abschließender Qualitätsdurchgang nach den ausgewählten Änderungen.

Nach Änderungen `/impeccable audit` erneut ausführen, um den Score zu vergleichen.

---
target: src/Einsatzueberwachung.Server/Components/Pages/Stammdaten.razor
total_score: 36
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T11-56-06Z
slug: erwachung-server-components-pages-stammdaten-razor
---
# Critique — Stammdaten (`/stammdaten`)

**Register:** product (Stammdaten-CRUD)
**As-found score:** 36/40 · P0 0 · P1 0 — **Scoped-CSS token-clean; Residuals markup/dynamische Daten**

## Assessment A — Design review

CRUD-Verwaltung für Personal, Hunde, Drohnen und Checklisten-Templates (Tabs). Kennzahlen-Karten, Import/Export/Vorlage, Formular + Tabelle je Tab, Deep-Link-Highlight (`mention-highlight-row`). Scoped-CSS sauber: nur `var(--ui-*, fallback)`-Muster (Token zuerst, Fallback als zweites Argument), keine `--bs-*`-Brand/Status-Leaks, keine bare Hardcodes. Nutzt das bevorzugte `.modal-modern`-Vokabular (konsistent mit EinsatzArchiv). Gute Zustandsabdeckung: Leerzustände („Keine Checklisten-Templates vorhanden", „Noch keine Items angelegt"), Status-Alert, Disabled-States beim Import.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Zähler-Karten, Status-Alert, Import-Disable |
| 2 | Realwelt-Bezug | 4 | Qualifikationen, Divera-ID, Szenario-Templates |
| 3 | Kontrolle & Freiheit | 3 | Abbrechen im Formular; Löschen aber neutral |
| 4 | Konsistenz & Standards | 4 | app-gethemte `.btn-*`, `.modal-modern`, `--ui-*` |
| 5 | Fehlervermeidung | 3 | „optional"-Badge, Form-Text-Hinweise |
| 6 | Wiedererkennung | 4 | klare Tabs, Status-Text, Skill-Labels |
| 7 | Flexibilität/Effizienz | 4 | Import/Export/Vorlage, Inline-Edit |
| 8 | Ästhetik/Minimalismus | 4 | ruhige Formulare/Tabellen |
| 9 | Fehlererkennung/-behebung | 3 | Leerzustände vorhanden; pro Personen-Tabelle keine Leerzeile |
| 10 | Hilfe & Doku | 4 | gute Form-Text-Erklärungen (z.B. Divera-ID) |

## Priorisierte Findings

- **P3 — Hardcodiertes `color:#fff` auf dynamischem Szenario-Badge** (`style="background-color:@Szenario.GetColorHex(); color:#fff;"`): bei hellen Szenariofarben Kontrastrisiko. Korrekte Lösung wäre eine luminanzbasierte On-Farbe (wie im Theme-Bootstrap), inline aber nicht token-fähig → Markup/dynamische Daten, dokumentiert. Szenario-Palette ist vermutlich kuratiert dunkel.
- **P3 — Destruktiv schwach signalisiert:** „Löschen" als `btn-outline-secondary` (wie EinsatzArchiv). Markup-Entscheidung → notiert.
- **P3 — Keine Leerzeile pro Personen-/Hunde-/Drohnen-Tabelle**, wenn Liste leer (Zähler-Karten zeigen 0). Minor.

## Re-Derivation
Scoped-CSS ohne Hardcodes (Token-Fallback-Muster korrekt); Inline-Farben nur dynamische Nutzerdaten. Hält unter NRW/Ruhr/Dark/Intensitäten. Kein Token-Eingriff nötig.

**Score nach Durchsicht:** 36/40.

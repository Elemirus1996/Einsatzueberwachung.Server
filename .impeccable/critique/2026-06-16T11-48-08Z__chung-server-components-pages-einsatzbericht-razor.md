---
target: src/Einsatzueberwachung.Server/Components/Pages/EinsatzBericht.razor
total_score: 35
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T11-48-08Z
slug: chung-server-components-pages-einsatzbericht-razor
---
# Critique — EinsatzBericht (`/einsatz-bericht`)

**Register:** product (Abschluss & Export)
**As-found score:** 35/40 · P0 0 · P1 0 — **Token-clean (kein eigenes Scoped-CSS), Findings verhaltensseitig**

## Assessment A — Design review

Berichts-/Abschlussseite: Kennzahlen-Karten (Ort/Teams/Notizen), Abschlussfelder (Ergebnis/Bemerkungen/GPS-Tracks-Option), Export (PDF/Excel) und Archivierungs-Aktionen mit Bestätigungsmodal. Reines Bootstrap-Vokabular über geteilte App-Klassen (`page-headline-row`, `stat-display-card`, `filter-card`); **kein eigenes `.razor.css`** → keine Off-Token-Hex/`--bs-*`-Tells. Status mehrkanalig: Bestätigungsmodal trägt bei „Reset" ein Warn-Icon **und** eine explizite Warn-Alert-Box; Export hat Spinner-Loadingzustand und Status-Alert.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Spinner „PDF wird erstellt", Status-Alert mit Pfad |
| 2 | Realwelt-Bezug | 4 | Ergebnis/Bemerkungen/Archiv-Sprache passend |
| 3 | Kontrolle & Freiheit | 4 | Confirm-Modal mit Abbrechen, destruktiver Reset getrennt |
| 4 | Konsistenz & Standards | 4 | Bootstrap-Semantik, geteilte Karten-Klassen |
| 5 | Fehlervermeidung | 4 | Reset-Warnung „nicht wiederherstellbar", Doppel-Klick-Guard |
| 6 | Wiedererkennung | 3 | Zwei PDF-Wege (Top-Link grün vs. Button „PDF erzeugen") |
| 7 | Flexibilität/Effizienz | 4 | PDF/Excel, Track-Einschluss optional |
| 8 | Ästhetik/Minimalismus | 3 | gesättigtes `btn-success` für Routine-Export etwas laut |
| 9 | Fehlererkennung/-behebung | 3 | kein Guard-/Leerzustand wenn kein aktiver Einsatz |
| 10 | Hilfe & Doku | 2 | wenig Inline-Hilfe zu Track-Einschluss-Folgen |

## Priorisierte Findings

- **P2 — Kein Guard-/Leerzustand ohne aktiven Einsatz:** Seite liest `CurrentEinsatz.*` direkt; ohne laufenden Einsatz Default-/Leerdaten. Verhaltensänderung außerhalb des CSS-Token-Sweeps → notiert, nicht angefasst.
- **P3 — Doppelter PDF-Pfad:** Top-Link „Als PDF exportieren" (grün, `/downloads/...`) und Button „PDF erzeugen" (`btn-primary`, `ExportAsync`) konkurrieren visuell/IA. Markup-/IA-Entscheidung → notiert.
- **P3 — `btn-success` gesättigt für Routine-Export:** leicht laut ggü. „leise per Default"; Bootstrap-Konvention & app-weit konsistent → belassen (kein Flatten einzelner Seite).

## Re-Derivation
Keine eigenen Farbwerte; erbt Theme/Intensität über Bootstrap-Semantik + geteilte Klassen → hält unter NRW/Ruhr/Dark. Kein Eingriff im Token-Scope.

**Score nach Durchsicht:** 35/40 (Token-clean; offene Punkte rein verhaltens-/IA-seitig).

---
target: src/Einsatzueberwachung.Server/Components/Pages/AuditLog.razor
total_score: 35
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T11-58-50Z
slug: eberwachung-server-components-pages-auditlog-razor
---
# Critique — AuditLog (`/audit-log`)

**Register:** product (Änderungsprotokoll)
**As-found score:** 35/40 · P0 0 · P1 0 — **Token-clean (kein Scoped-CSS); Residuals app-weit/IA**

## Assessment A — Design review

Schlankes Protokoll des laufenden Einsatzes: Such-/Kategoriefilter, Treffer-Zähler, Tabelle (Zeit/Kategorie/Aktion/Quelle/Details) mit kategorialen Badges, Löschen mit Bestätigung. Kein eigenes Scoped-CSS → keine Off-Token-Tells. Status **mehrkanalig** (Badge trägt immer das Kategorie-Label *und* die Farbe). Gute Zustandsabdeckung: leer („Noch keine Einträge"), filter-leer („Keine Einträge für diesen Filter"), Live-Filter (`oninput`).

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Treffer-Zähler, Live-Filter |
| 2 | Realwelt-Bezug | 4 | Kategorien Einsatz/Team/Notiz/System |
| 3 | Kontrolle & Freiheit | 3 | Löschen mit Confirm; Confirm-Button primary statt danger |
| 4 | Konsistenz & Standards | 3 | Bootstrap `.modal` statt `.modal-modern` (app-weit uneinheitlich) |
| 5 | Fehlervermeidung | 4 | Confirm mit Anzahl „Alle N Einträge wirklich löschen?" |
| 6 | Wiedererkennung | 4 | Label+Farbe-Badges, monospaced Zeitstempel |
| 7 | Flexibilität/Effizienz | 4 | Suche + Kategorie kombinierbar, 500er-Limit |
| 8 | Ästhetik/Minimalismus | 4 | ruhige, dichte Tabelle |
| 9 | Fehlererkennung/-behebung | 3 | klare Leer-/Filtertexte; kein Lade-Spinner |
| 10 | Hilfe & Doku | 2 | wenig Kontexthilfe (selbsterklärend) |

## Priorisierte Findings

- **P3 — Kategoriale Badges nutzen Bootstrap-Utilities** (`bg-primary/-success/-info/-secondary` = Bootstrap-Hues, nicht Brand/`--signal-*`). Da rein kategorial **mit Label** → ausreichend unterscheidbar, kein Statussignal. Brand-Mapping bräuchte Markup/Utility-Ergänzung → notiert, belassen.
- **P3 — Modal-Vokabular:** Bootstrap `.modal show d-block` statt des app-bevorzugten `.modal-modern` (wie Stammdaten/EinsatzArchiv-Löschen). App-weite IA-Vereinheitlichung als eigene Aufgabe → dokumentiert.
- **P3 — Destruktiver Confirm `btn-primary`** (brand) statt danger. Markup-Entscheidung → notiert.

## Re-Derivation
Kein eigenes CSS; erbt Theme/Intensität über Bootstrap + app-gethemte Klassen. Hält unter NRW/Ruhr/Dark. Kein Token-Eingriff nötig.

**Score nach Durchsicht:** 35/40.

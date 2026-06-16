---
target: src/Einsatzueberwachung.Server/Components/Pages/MobileKarte.razor
total_score: 35
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T12-11-32Z
slug: rwachung-server-components-pages-mobilekarte-razor
---
# Critique — MobileKarte (`/mobile-karte`)

**Register:** product (mobile Read-only-Karte)
**As-found score:** 35/40 · P0 0 · P1 0 — **Clean pass; keine Änderung nötig**

## Assessment A — Design review

Schlanke Lesemodus-Karte für das Smartphone: Toolbar (Titel, „Zentrieren", Team-Zähler-Badge), Leaflet-Canvas, Leerzustand. Kein eigenes Scoped-CSS (Klassen aus Layout/global). Leerzustand mehrkanalig (Karten-Icon + „Kein Einsatz aktiv"). Suchgebiete werden mit ihrer benutzerdefinierten Farbe gerendert; Leaflet übernimmt Touch-Pan/Zoom als primäre Interaktion.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Team-Zähler, Lesemodus-Label |
| 2 | Realwelt-Bezug | 4 | Suchgebiete mit Team-Zuordnung |
| 3 | Kontrolle & Freiheit | 3 | Zentrieren + Leaflet-Gesten |
| 4 | Konsistenz & Standards | 4 | Badge/Button-Standard |
| 5 | Fehlervermeidung | 4 | reiner Lesemodus |
| 6 | Wiedererkennung | 3 | Polygon-Label „Gebiet (Team)" |
| 7 | Flexibilität/Effizienz | 3 | Auto-Update bei Änderungen |
| 8 | Ästhetik/Minimalismus | 4 | sehr fokussiert |
| 9 | Fehlererkennung/-behebung | 3 | Leerzustand klar |
| 10 | Feldtauglichkeit | 3 | Karte touch-fähig; ein btn-sm |

## Priorisierte Findings

- **Keine P0/P1.** Read-only, Leerzustand mehrkanalig, Leaflet-Touch als Hauptinteraktion.
- **P3 — „Zentrieren" als `btn-sm`** (~31px): einzelne Neben-Aktion; Leaflet-Gesten sind primär → belassen.
- **P3 — Polygon-Fallback `#3388ff`** ist ein Daten-Default pro Suchgebiet (Leaflet-Stroke), kein UI-Token → korrekt belassen.

## Re-Derivation
Kein eigenes CSS; erbt Theme/Dark. Polygonfarben sind Nutzerdaten. Kein Token-Eingriff nötig.

**Score nach Durchsicht:** 35/40.

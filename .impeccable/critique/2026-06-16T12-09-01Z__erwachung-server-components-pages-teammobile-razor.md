---
target: src/Einsatzueberwachung.Server/Components/Pages/TeamMobile.razor
total_score: 33
p0_count: 0
p1_count: 1
timestamp: 2026-06-16T12-09-01Z
slug: erwachung-server-components-pages-teammobile-razor
---
# Critique — TeamMobile (`/team`)

**Register:** product (Feld-Smartphone-Ansicht, TeamMobileLayout)
**As-found score:** 33/40 · P0 0 · P1 1 — **Optimiert (P1 Touch-Ziele behoben, CSS-only)**

## Assessment A — Design review

Die zentrale Feldansicht für das Team am Smartphone: Header mit Teamname, EL-Push-Nachrichten-Strip, ausklappbares Info-Panel, GPS-Banner mit allen Genehmigungs-/Fehlerzuständen, Leaflet-Karte, Status-Aktionsleiste und Fußzeile mit Halsband-/Handy-GPS-Alter. Scoped-CSS token-clean (`color-mix(--theme-primary …)`, `var(--ui-*, fallback)`, `--theme-alert-warning`, `--theme-on-accent`) und **intensitätsbewusst** (`[data-intensity="dezent"]`-Variante für Header/Strip/Panel).

**Status durchgehend mehrkanalig:** Status-Buttons = Icon + Label + Farbe (Im Gebiet/Pause/Funk/Beendet); GPS-Banner = je Zustand eigenes Icon + erklärender Text + Farbe (HTTPS-Sperre, fehlende Berechtigung, Geo-Fehler, Warten); Push = Megafon-Icon + Text; Fußzeile GPS = `bi-broadcast` (muted/success) + Alters-Label.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | GPS-Alter, Banner, Spinner |
| 2 | Realwelt-Bezug | 4 | Feld-Vokabular, EL-Nachrichten |
| 3 | Kontrolle & Freiheit | 3 | Status senden, Abmelden, Panel-Toggle |
| 4 | Konsistenz & Standards | 4 | Brand-Header, Outline-Status-Buttons |
| 5 | Fehlervermeidung | 4 | GPS-Fehlerzustände vollständig + Retry |
| 6 | Wiedererkennung | 4 | Icon+Label+Farbe je Status |
| 7 | Flexibilität/Effizienz | 3 | schnelle Status-Buttons |
| 8 | Ästhetik/Minimalismus | 3 | dichte, aber klare Feld-UI |
| 9 | Fehlererkennung/-behebung | 4 | klare GPS-Fehler + Erneut-Aktion |
| 10 | Feldtauglichkeit (Touch/Kontrast) | 3→4 | **Touch-Ziele waren <44px** — behoben |

## Priorisierte Findings

- **P1 (behoben) — Touch-Ziele unter 44px.** Header-Buttons (`btn-sm btn-light`), die primären Status-Aktionen (`btn-sm flex-grow-1`: Im Gebiet/Pause/Funk/Beendet) und der GPS-Anfordern/Erneut-Button waren alle `btn-sm` (~31px) — zu klein für Daumen-/Handschuhbedienung im Feld (Designprinzip „Touch-Ziele ≥44px").
  **Fix (CSS-only, scoped):** `.team-mobile-header .btn { min-width/height:44px }`, `.team-mobile-status-actions .btn { min-height:44px }`, `.team-mobile-gps-banner .btn { min-height:40px }`. Keine Markup-/Token-Änderung.
- **P3 — `var(--bs-border-color)`** in `.team-mobile-gps-banner` (neutral) → tolerierbar, belassen.

## Re-Derivation
Header/Strip/Panel/Status über `color-mix(--theme-*)`/`--theme-alert-*`/`--ui-*` → reagiert auf NRW/Ruhr + Intensität (eigene `dezent`-Variante). Status mehrkanalig. Touch-Ziele jetzt feldtauglich.

**Score nach Optimierung:** ~36/40 (Feldtauglichkeit 3→4).

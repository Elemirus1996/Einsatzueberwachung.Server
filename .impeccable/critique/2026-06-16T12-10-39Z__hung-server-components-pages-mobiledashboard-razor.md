---
target: src/Einsatzueberwachung.Server/Components/Pages/MobileDashboard.razor
total_score: 34
p0_count: 0
p1_count: 1
timestamp: 2026-06-16T12-10-39Z
slug: hung-server-components-pages-mobiledashboard-razor
---
# Critique — MobileDashboard (`/mobile-dashboard`)

**Register:** product (mobile Übersicht, Read-only)
**As-found score:** 34/40 · P0 0 · P1 1 — **Optimiert (Timer-Mehrkanal behoben, CSS-only)**

## Assessment A — Design review

Kompakte mobile Lage-Übersicht: Einsatzstatus-Karte, Team-Schnellübersicht (max. 6 + „weitere"), Link zur Mobile-PWA, Desktop-Shortcuts. Scoped-CSS minimal und token-clean (`--ui-tile-border`, `--ui-tile-bg`). Status-Badges mehrkanalig (`bg-success "Aktiv"` / `bg-secondary "Kein Einsatz"` = Farbe + Label).

**Kern-Defekt:** Die Team-Timer-Eskalation (`text-success` → `text-warning fw-bold` → `text-danger fw-bold`) unterscheidet **Erst-Warnung (orange)** und **Zweit-Warnung/kritisch (rot)** nur über die Farbe — beide sind `fw-bold`. Das ist exakt der im Projekt benannte Timer-Testfall (Grün→Orange→Rot bei Farbsehschwäche/Sonnenlicht).

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | aktiv/inaktiv, Team-Zähler |
| 2 | Realwelt-Bezug | 4 | Ort/Alarmiert/Teams |
| 3 | Kontrolle & Freiheit | 3 | Links zu Monitor/Karte/PWA |
| 4 | Konsistenz & Standards | 4 | Brand-Buttons, list-group |
| 5 | Fehlervermeidung | 3 | Hinweis „Desktop zum Anlegen" |
| 6 | Status-Mehrkanal (Timer) | 2→4 | **orange/rot nur Farbe** — behoben |
| 7 | Flexibilität/Effizienz | 3 | Top-6-Teams + Überlauf |
| 8 | Ästhetik/Minimalismus | 4 | ruhige Karten, Monospace-Timer |
| 9 | Fehlererkennung/-behebung | 3 | Leerzustand vorhanden |
| 10 | Hilfe & Doku | 3 | erklärt PWA-Funktionen |

## Priorisierte Findings

- **P1 (behoben) — Timer orange vs. rot nur über Farbe.** Erst- und Zweit-Warnung waren beide `fw-bold`, nur Hue unterschied. **Fix (CSS-only, scoped):** `.mobile-dashboard-timer.text-danger { animation: mobile-timer-critical … }` — dezenter Opazitäts-Puls gibt dem kritischen Zustand einen Bewegungskanal (konsistent mit dem Blink-Muster im Haupt-Monitor). Damit: grün=statisch, orange=statisch+fett, rot=fett+pulsierend → auch ohne Farbe unterscheidbar.
- **P3 — Desktop-Shortcuts `btn-sm`** (Monitor/Karte): sekundäre Navigation, primäre Aktion „Mobile-App öffnen" ist `w-100` voll groß → belassen.

## Re-Derivation
Karten/Badges über `--ui-tile-*`/Bootstrap-Semantik; erbt Theme/Dark. Timer jetzt mehrkanalig (Farbe + Gewicht + Bewegung). Kein Hardcoded-Hex.

**Score nach Optimierung:** ~37/40 (Timer-Mehrkanal 2→4).

---
target: src/Einsatzueberwachung.Server/Components/Pages/Einstellungen.razor
total_score: 36
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T12-01-41Z
slug: achung-server-components-pages-einstellungen-razor
---
# Critique — Einstellungen (`/einstellungen`)

**Register:** product (Konfiguration, art-direktierte „Akten/Register"-UI)
**As-found score:** 36/40 · P0 0 · P1 0 — **2 Token-Leaks behoben; art-direktiertes `.cfg-*`-System erhalten**

## Assessment A — Design review

Umfangreiche Einstellungsseite als bewusst gestaltete **Akten-/Registermetapher** mit eigenem `.cfg-*`-Komponentensystem (Register-Tabs links, Pillen, Range-Slider, Ghost-/Primary-Buttons, Theme-Preview-Mock). Durchgehend token-getrieben über `--primary-color`, `--ui-*`, `--danger-color` und `color-mix`-Ableitungen — sauber, konsistent, klar intentional (passt zum Register „Werkzeug, kein Marketing"). Nicht flach gemacht.

### Befund (vor Fix)
- **`.ks-recorder-input.is-recording`** (aktiver Tastenkürzel-Aufnahme-Zustand) nutzte rohe Bootstrap-Variablen: `--bs-primary` (Blau #0d6efd), `rgba(var(--bs-primary-rgb),.25)`, `--bs-primary-bg-subtle` — **inkonsistent** zur brand-`--primary-color`-Aktiv-Sprache des Rests (`.cfg-pill.is-active`, `.cfg-btn-primary`).
- Drei `#fff`-Literale als On-Farbe auf `--primary-color`-Fills (`.cfg-save-btn`, `.cfg-pill.is-active`, `.cfg-btn-primary`).

### Heuristik-Scores (0–4)
| # | Heuristik | vorher | nachher |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | 4 |
| 2 | Realwelt-Bezug | 4 | 4 |
| 3 | Kontrolle & Freiheit | 4 | 4 |
| 4 | Konsistenz & Standards | 3 | 4 |
| 5 | Fehlervermeidung | 3 | 3 |
| 6 | Wiedererkennung | 4 | 4 |
| 7 | Flexibilität/Effizienz | 4 | 4 |
| 8 | Ästhetik/Minimalismus | 4 | 4 |
| 9 | Fehlererkennung/-behebung | 3 | 4 |
| 10 | Hilfe & Doku | 3 | 3 |

## Angewandte Fixes (CSS-only, `Einstellungen.razor.css`)
- `.ks-recorder-input.is-recording`: `--bs-primary` → `--primary-color`; Glow → `color-mix(--primary-color 25%, transparent)`; bg → `--theme-primary-soft`. **Jetzt brand-/intensitäts-/dark-adaptiv und konsistent mit `.cfg-pill.is-active`.**
- `#fff` → `--theme-on-accent` auf `.cfg-save-btn`, `.cfg-pill.is-active`, `.cfg-btn-primary` (visuell identisch, token-korrekt).

## Bewusst belassen
- `rgba(255,255,255,0.75)` in `.theme-preview-nav-link`: sitzt auf dem **fixiert-dunklen** Nav-Vorschau-Mock (`--ui-sidebar-bg`) — korrekt (Vorschau der stets dunklen Navigation), analog zum Lage-/Funk-Muster.
- `rgba(0,0,0,0.12)` Elevation-Shadow: neutrale Tiefe, keine Markenfarbe.

## Re-Derivation
Alle Stati leiten über `--primary-color`/`--ui-*`/`--theme-*` ab → hält unter NRW + Ruhr + Dark + Intensitäten; Aufnahme-Zustand folgt der Marke statt Bootstrap-Blau.

**Score nach Fix:** ~38/40.

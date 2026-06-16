---
target: src/Einsatzueberwachung.Server/Components/Pages/Trainer.razor
total_score: 37
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T12-05-30Z
slug: ueberwachung-server-components-pages-trainer-razor
---
# Critique — Trainer (`/trainer`, TrainerLayout, passwortgeschützt)

**Register:** product (art-directed „Trainer-Konsole", iPad/PWA-tauglich)
**As-found score:** 37/40 · P0 0 · P1 0 — **Clean pass; keine Änderung nötig**

## Assessment A — Design review

Passwortgeschützter Trainer-Arbeitsplatz mit eigenem Layout. Bewusst inszenierte Oberfläche: Brand-abgeleiteter dunkler Shell (`radial-`/`linear-gradient` aus `color-mix(--theme-primary/--theme-secondary …)`), darauf schwebende helle Karten. Konsequent feldtauglich: **Touch-Ziele ≥48px** (52px auf Tablet), `env(safe-area-inset-*)` für PWA, eigener responsiver Grid-Stack. Status mehrkanalig: Tab-Aktivitäts-Badge (`--success-color` + Blink-Bewegung), Team-Reaktions-Box (`--warning-color` + Rahmen+Hintergrund+Label). **Eigener `[data-bs-theme="dark"]`-Block** entkoppelt Kartenflächen sauber vom Shell.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | aktiver Tab, Live-Badge, KPI-Kacheln |
| 2 | Realwelt-Bezug | 4 | Briefing/Szenario/Log-Vokabular |
| 3 | Kontrolle & Freiheit | 4 | Tab-Navigation, deaktivierte Zustände sichtbar |
| 4 | Konsistenz & Standards | 4 | durchgängig color-mix auf Theme-Tokens |
| 5 | Fehlervermeidung | 3 | `:disabled` klar markiert |
| 6 | Wiedererkennung | 4 | aktiver Tab via Farbe+Unterstrich+Hintergrund |
| 7 | Flexibilität/Effizienz | 4 | Tablet/PWA-optimiert, sticky Tabs |
| 8 | Ästhetik/Minimalismus | 4 | kohärente Inszenierung, ruhige Karten |
| 9 | Fehlererkennung/-behebung | 3 | Login-Karte separat |
| 10 | Hilfe & Doku | 3 | Kicker/Eyebrow-Beschriftung |

## Priorisierte Findings

- **Keine P0/P1.** Art-directed Fläche; Intensität wirkt über `color-mix(--theme-*)`. Status mehrkanalig, Dark-Mode dediziert, Touch ≥48px.
- **P3 — hartkodierte Dark-Surfaces** (`#1f2530`, `#2a3441`, `#edf2f6`, `#b8c6d3`) im Dark-Block. Nur Dark-Mode-Override, entkoppeln Karten bewusst vom Shell → tolerierbar; ließe sich optional aus `--ui-surface-2`/`--ui-text` ableiten. Belassen.

## Re-Derivation
Shell, Karten, Tabs, Status über `color-mix` auf `--theme-*`/`--success-color`/`--warning-color` → reagiert auf NRW/Ruhr + Intensität. Dark-Mode separat behandelt. Kein Token-Eingriff nötig.

**Score nach Durchsicht:** 37/40.

---
target: src/Einsatzueberwachung.Server/Components/Pages/Lage.razor
total_score: 33
p0_count: 0
p1_count: 1
timestamp: 2026-06-16T11-41-39Z
slug: atzueberwachung-server-components-pages-lage-razor
---
# Critique — Lage (`/Lage`)

**Register:** product (Lagewand / Wand-Display, eigenes `LageLayout`)
**As-found score:** 33/40 · P0 0 · P1 1

## Assessment A — Design review

Großflächiges, projektionstaugliches Lagebild: Statusband (ID, Teams/Drohnen, Live-Uhr, Puls-Dot), Einsatz-Info-Kacheln mit großen tabellarischen Zahlen, Karte, optionales Drohnen-Stream-Grid. Eigener, bewusst dunkler `--lage-*`-Tokensatz, theme-abgeleitet (`color-mix(--theme-secondary/-primary/-warning …)`) → reagiert auf NRW/Ruhr. Intensitätsbewusst (dezent reduziert Linien + stoppt Puls). Klare, ruhige Wand-Ästhetik, kein Slop. Mehrkanaliger Live-Indikator (Dot + „Live"-title + Puls).

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Live-Uhr, Puls-Dot, Team/Drohnen-Zähler, Einsatzdauer |
| 2 | Realwelt-Bezug | 4 | Lagewand-Vokabular, Domänenzahlen |
| 3 | Kontrolle & Freiheit | 3 | Read-only-Display (zweckgemäß) |
| 4 | Konsistenz & Standards | 3 | Ink an `--ui-text` gekoppelt → bricht im Light-Theme (s. P1) |
| 5 | Fehlervermeidung | 3 | Display, wenig Interaktion |
| 6 | Wiedererkennung | 4 | Große beschriftete Kennzahlen |
| 7 | Flexibilität/Effizienz | 3 | Auto-Layout je Drohnenanzahl (`data-streams`) |
| 8 | Ästhetik/Minimalismus | 4 | Reduzierte, projektionstaugliche Wand |
| 9 | Fehlererkennung/-behebung | 3 | Leerzustände (0 Drohnen) sauber behandelt |
| 10 | Hilfe & Doku | 3 | Selbsterklärend als Anzeige |

## Priorisierte Findings

- **P1 — Dark-on-dark im Light-Theme (Kontrast/Robustheit):** Die Lagewand ist konzeptionell IMMER dunkel (`--lage-bg` mit `#0e1418` verankert), aber `--lage-ink` leitete von `var(--ui-text)` ab. `:root` (Default) ist das **Light**-Palette mit dunklem `--ui-text #1f2937` → dunkle Schrift auf dunklem Hintergrund (~1.3:1) sobald nicht explizit Dark-Mode aktiv. `LageLayout` erzwingt kein Dark. → **behoben:** `--lage-ink` auf fixe helle Ableitung (`color-mix(#e6eef2 88%, #f7fbff 12%)`) entkoppelt; gesamte Ink-Kette (muted/dim/line) erbt den Fix.
- **P3 — `lage-info-title` muted-uppercase:** als Wand-Eyebrow über großen Zahlen bewusst leise → belassen.

## Re-Derivation
Nach Fix bleibt die Wand in beiden App-Themes hell-auf-dunkel lesbar; Akzente/Hintergründe weiterhin theme-abgeleitet (NRW/Ruhr) und intensitätsbewusst.

**Erwartet nach Fix:** ~37/40 (Konsistenz 3→4, P1 beseitigt).

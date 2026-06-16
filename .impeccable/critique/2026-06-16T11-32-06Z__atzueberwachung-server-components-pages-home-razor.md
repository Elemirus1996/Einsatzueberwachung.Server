---
target: src/Einsatzueberwachung.Server/Components/Pages/Home.razor
total_score: 33
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T11-32-06Z
slug: atzueberwachung-server-components-pages-home-razor
---
# Critique — Home (`/`)

**Register:** product (Lagezentrum-Startseite / Bereitschaftsraum)
**As-found score:** 33/40 · P0 0 · P1 0

## Assessment A — Design review

Drei-Spalten-Kommandolayout: **Bereitschaft** (Notizen) · **Lagebild** (Logo, Live-Uhr, Status-Band, zustandsabhängige CTA) · **Letzte Lagen + Bilanz**. Ruhige, kalte Default-Haltung, ein einziger primärer CTA, der je Lage wechselt (Alarm→anlegen, aktiv→Monitor, ruhig→neu). Kein AI-Slop: echte operative Stimme, eigenständige Kartensemantik, monospace-Uhr als Fokus.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Status-Band (3 Zustände), Live-Uhr, Versionsfuß |
| 2 | Realwelt-Bezug | 4 | Deutsch, Domänenbegriffe Bereitschaft/Lage/Einsatz/Übung |
| 3 | Kontrolle & Freiheit | 3 | Notiz add/del; Löschen ohne Rückfrage (geringes Risiko) |
| 4 | Konsistenz & Standards | 2 | **Bootstrap-Default-Blau-Leak** in is-active/is-uebung; #fff/rgba-Hardcodes |
| 5 | Fehlervermeidung | 3 | maxlength 200; Logo onerror behandelt |
| 6 | Wiedererkennung | 4 | Alles beschriftet, Icon+Text |
| 7 | Flexibilität/Effizienz | 3 | Enter zum Hinzufügen; zustandsabhängige CTA |
| 8 | Ästhetik/Minimalismus | 4 | Ruhiger Rhythmus, klare Hierarchie |
| 9 | Fehlererkennung/-behebung | 3 | Weitgehend n/a |
| 10 | Hilfe & Doku | 3 | Selbsterklärend |

## Priorisierte Findings

- **P2 — Off-Token-Farben (Konsistenz):** `.home-status-band.is-active` und `.home-timeline-item.is-uebung .home-timeline-dot` nutzten `--bs-primary-bg-subtle` mit Fallback `rgba(13,110,253,…)` — Bootstrap-Default-Blau statt Markenfarbe; unter NRW/Ruhr falsch. `is-calm`/`is-einsatz` mit hartem grün/rot-rgba, `is-alarm` mit `color:#fff`. → **behoben:** auf `--theme-primary-soft/-border`, `--success-soft/-border`, `--danger-soft`, `--danger-on-fill` umgestellt; Alarm-Keyframe-Glow auf `color-mix(--danger-color …)`.
- **P3 — Notiz-Löschen ohne Bestätigung:** geringes Risiko (Kurz-Notiz), belassen.
- **P3 — "Alle →" als `text-muted`-Link:** sekundäre Navigation, akzeptabel.

## Re-Derivation
Nach Fix folgen alle Zustandsfarben dem Token-System → hält unter NRW + Ruhr + Dark + dezent/ausgewogen/lebhaft. Status weiterhin mehrkanalig (Icon + Puls + Label + Farbe).

**Erwartet nach Fix:** ~36/40 (Konsistenz 2→4).

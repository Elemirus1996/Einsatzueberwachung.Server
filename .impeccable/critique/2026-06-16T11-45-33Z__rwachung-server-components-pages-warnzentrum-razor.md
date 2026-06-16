---
target: src/Einsatzueberwachung.Server/Components/Pages/Warnzentrum.razor
total_score: 37
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T11-45-33Z
slug: rwachung-server-components-pages-warnzentrum-razor
---
# Critique — Warnzentrum (`/warnzentrum`)

**Register:** product (Warn-/Alarmzentrale)
**As-found score:** 37/40 · P0 0 · P1 0 — **Clean pass, keine Änderung nötig**

## Assessment A — Design review

Fokussierte Warnzentrale: Liste der Warnungen (BEM `warn-list`), je Eintrag Icon + Level-Farbe + Modifier-Klasse (`--critical/--warning/--info`) → konsequent **mehrkanaliger** Status (Icon-Form *und* Farbe *und* Klasse). Konfigurierbares Regel-Panel (Tabelle: Quelle/Beschreibung/Cooldown/Aktiv/Warnstufe), Leerzustand („Keine Warnungen"), Quell-Navigation je Eintrag. Durchgehend token-getrieben (`--theme-alert-*`, abgeleitet aus `--signal-*`), **intensitätsbewusst** (`dezent` → transparenter Hintergrund, Status trägt über Icon/Text).

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Live-Add via Event, Zähler-Badge, Zeitstempel |
| 2 | Realwelt-Bezug | 4 | Warnstufen-/Quellen-Vokabular |
| 3 | Kontrolle & Freiheit | 3 | Dismiss/ClearAll/Navigate; ClearAll ohne Rückfrage |
| 4 | Konsistenz & Standards | 4 | Token-getrieben, mehrkanalig, intensitätsbewusst |
| 5 | Fehlervermeidung | 3 | Cooldown 0–3600 begrenzt; ClearAll ohne Confirm |
| 6 | Wiedererkennung | 4 | Icon+Farbe+Label je Level, Regeltabelle beschriftet |
| 7 | Flexibilität/Effizienz | 4 | Pro Quelle Regeln/Override/Cooldown, Speichern-Feedback |
| 8 | Ästhetik/Minimalismus | 4 | Ruhig, klar, keine Dekoration |
| 9 | Fehlererkennung/-behebung | 4 | Leerzustand „alles ruhig", Quell-Navigation |
| 10 | Hilfe & Doku | 3 | title-Hinweise (z.B. „0 = bei jedem Ereignis") |

## Priorisierte Findings

- **Keine P0/P1/P2.** Seite ist bereits exemplarisch token-/intensitätskonform.
- **P3 — Warning-Level-Text in `dezent` (amber auf hell):** `color-mix(--warning-color 88%, --ui-text)` auf transparentem Hintergrund kann grenzwertig (~3:1) sein; Icon-Form trägt den Status mit. Konsistent mit dem bewussten Intensitäts-Design → belassen.
- **P3 — `GetLevelColor` nutzt Bootstrap `text-danger/-info/-warning`** statt `--signal-*` in der Regeltabelle: semantisch korrekt, geringe Inkonsistenz → belassen.
- **P3 — ClearAll ohne Bestätigung:** außerhalb des Token-Scopes → belassen.

## Re-Derivation
Hält bereits unter NRW + Ruhr + Dark + allen Intensitäten; Status mehrkanalig. Kein Eingriff.

**Score nach Durchsicht:** 37/40 (unverändert, bestätigt gut).

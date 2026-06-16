---
target: src/Einsatzueberwachung.Server/Components/Pages/PopoutTeams.razor
total_score: 36
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T12-14-06Z
slug: rwachung-server-components-pages-popoutteams-razor
---
# Critique — PopoutTeams (`/popout-teams`)

**Register:** product (Popout-Fenster, Sekundärmonitor)
**As-found score:** 36/40 · P0 0 · P1 0 — **Clean pass; keine Änderung nötig**

## Assessment A — Design review

Separates Fenster mit Team-Tabelle (Team, Laufzeit, Warnstufe, Suchgebiet) und Live-Update. Kein eigenes Scoped-CSS. **Vorbildlich mehrkanalig:** Der Timer-Eskalationsstatus wird zugleich als farbiges Badge (`bg-success`/`bg-warning text-dark`/`bg-danger`) **und** als eigene Spalte „Warnstufe" mit Klartext „Grün/Orange/Rot" geführt — vollständig farbsehschwäche-sicher (genau das im Projekt geforderte zweite Kanal-Muster für den Timer-Testfall).

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Live-Timer + Warnstufe |
| 2 | Realwelt-Bezug | 4 | Team/Suchgebiet-Tabelle |
| 3 | Kontrolle & Freiheit | 3 | Link zurück zum Monitor |
| 4 | Konsistenz & Standards | 4 | Standard-Tabelle, Brand-Button |
| 5 | Fehlervermeidung | 3 | Leerzustand „Keine Teams aktiv" |
| 6 | Status-Mehrkanal (Timer) | 4 | Badge-Farbe **+** Klartext-Spalte |
| 7 | Flexibilität/Effizienz | 3 | sortiert nach Teamname |
| 8 | Ästhetik/Minimalismus | 4 | reduziert, monitor-tauglich |
| 9 | Fehlererkennung/-behebung | 3 | Leerzustand klar |
| 10 | Hilfe & Doku | 4 | Untertitel erklärt Fensterzweck |

## Priorisierte Findings

- **Keine P0/P1.** Mustergültige Timer-Mehrkanaligkeit (Farbe + Wort). Kein Off-Token-Color.

## Re-Derivation
Kein eigenes CSS; Bootstrap-Status-Badges + Klartext-Spalte; erbt Theme/Dark. Kein Token-Eingriff nötig.

**Score nach Durchsicht:** 36/40.

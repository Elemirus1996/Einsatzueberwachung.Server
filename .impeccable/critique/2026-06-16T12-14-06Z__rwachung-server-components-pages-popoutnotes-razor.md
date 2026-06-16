---
target: src/Einsatzueberwachung.Server/Components/Pages/PopoutNotes.razor
total_score: 35
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T12-14-06Z
slug: rwachung-server-components-pages-popoutnotes-razor
---
# Critique — PopoutNotes (`/popout-notes`)

**Register:** product (Popout-Fenster, Notizen & Funksprüche)
**As-found score:** 35/40 · P0 0 · P1 0 — **Clean pass; keine Änderung nötig**

## Assessment A — Design review

Separates Fenster für Notizen/Funksprüche mit Filter (Alle/Notizen/Funk), Aktualisieren, Eintragskarten inkl. Antwort-Threads und Inline-Antwortfeld. Kein eigenes Scoped-CSS; durchgängig Standard-Bootstrap + Brand-Outline-Buttons, `@mention`-Rendering. Quelle je Eintrag als Klartext (Teamname bzw. „System"). Vollständige Zustände: Leerzustand („Keine Einträge vorhanden"), Liste, verschachtelte Antworten mit `border-start`-Einzug.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Live-Update, Zeitstempel |
| 2 | Realwelt-Bezug | 4 | Funk/Notiz-Vokabular |
| 3 | Kontrolle & Freiheit | 4 | Filter, Aktualisieren, Antworten |
| 4 | Konsistenz & Standards | 4 | Standard-Karten/Buttons |
| 5 | Fehlervermeidung | 3 | Leerzustand vorhanden |
| 6 | Wiedererkennung | 3 | Quelle als Klartext, @-Mentions |
| 7 | Flexibilität/Effizienz | 4 | Typ-Filter |
| 8 | Ästhetik/Minimalismus | 4 | ruhige Eintragskarten |
| 9 | Fehlererkennung/-behebung | 3 | klare Leeransicht |
| 10 | Hilfe & Doku | 2 | Untertitel erklärt Zweck |

## Priorisierte Findings

- **Keine P0/P1.** Token-clean, vollständige Zustände, Antwort-Threads sauber eingerückt.
- **P3 — Eintragstyp (Notiz vs. Funk) ohne Karten-Chip:** Der Filter trennt, die Karte selbst trägt kein Typ-Label/Icon. Optional ein kleines Chip („Funk"/„Notiz") für schnelleres Scannen. Belassen.

## Re-Derivation
Kein eigenes CSS; erbt Theme/Dark über Bootstrap/forms.css. Kein Off-Token-Color. Kein Token-Eingriff nötig.

**Score nach Durchsicht:** 35/40.

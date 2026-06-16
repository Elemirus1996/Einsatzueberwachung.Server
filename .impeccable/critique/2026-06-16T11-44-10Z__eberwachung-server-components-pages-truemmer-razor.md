---
target: src/Einsatzueberwachung.Server/Components/Pages/Truemmer.razor
total_score: 34
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T11-44-10Z
slug: eberwachung-server-components-pages-truemmer-razor
---
# Critique — Truemmer (`/truemmer`)

**Register:** product (Trümmer-Lagekarte, pixel-basiert ohne GPS)
**As-found score:** 34/40 · P0 0 · P1 0

## Assessment A — Design review

Fokussiertes Werkzeug: Drohnenfoto hochladen → Suchgebiete pixel-basiert einzeichnen, Team/Farbe/Name je Gebiet, Karten-Liste. Standard-Bootstrap-Bausteine (card, list-group, alert, InputFile), die global gethemt sind. Saubere Szenario-Gate (nur im Trümmer-Szenario), Leerzustände vorhanden („Noch kein Bild", „Noch keine Suchgebiete"). Klarer, ruhiger Aufbau, kein Slop.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 3 | Status-Alert, Upload-Disabled, Zeichenmodus-Hinweis |
| 2 | Realwelt-Bezug | 4 | Trümmerfeld/Suchgebiet-Vokabular |
| 3 | Kontrolle & Freiheit | 3 | Zeichnen abbrechen, Löschen; Löschen ohne Rückfrage |
| 4 | Konsistenz & Standards | 3 | list-group active = Bootstrap-Blau; `--bs-border-color` statt `--ui-border` |
| 5 | Fehlervermeidung | 3 | accept-Filter, Szenario-Gate; kein Lösch-Confirm |
| 6 | Wiedererkennung | 4 | Karten-/Gebiete-Listen beschriftet, Icons |
| 7 | Flexibilität/Effizienz | 3 | Inline-Bearbeitung (Name/Team/Farbe), Multi-Karten |
| 8 | Ästhetik/Minimalismus | 3 | Funktional-schlicht, Standard-BS-Optik |
| 9 | Fehlererkennung/-behebung | 3 | Fehler-Alert; Upload-Fehler sichtbar |
| 10 | Hilfe & Doku | 4 | Inline-Hinweise erklären Zeichnen/Upload |

## Priorisierte Findings

- **P2 — list-group active = Bootstrap-Blau (systemisch):** `--bs-list-group-active-bg` blieb global `#0d6efd` (nirgends überschrieben), also aktiver Karten-Eintrag bootstrap-blau statt Markenfarbe; die Seite korrigierte nur die `small`-Schrift. → **behoben global** in `forms.css`: `.list-group { --bs-list-group-active-bg: var(--theme-primary); …-color: var(--theme-on-accent); }` — wirkt auf alle list-group-Seiten, luminanzkorrekte Schrift.
- **P3 — `--bs-border-color` am Map-Host:** → **behoben:** `--ui-border`.
- **P3 — Lösch-Aktionen (Karte/Gebiet) ohne Bestätigung:** Datenverlust-Risiko moderat; nicht geändert (außerhalb des Token-Scopes dieses Durchlaufs).

## Re-Derivation
Aktiver Listeneintrag jetzt markenfarbig (NRW/Ruhr) mit luminanzkorrekter Schrift; Map-Host-Border tokenkonsistent.

**Erwartet nach Fix:** ~36/40 (Konsistenz 3→4).

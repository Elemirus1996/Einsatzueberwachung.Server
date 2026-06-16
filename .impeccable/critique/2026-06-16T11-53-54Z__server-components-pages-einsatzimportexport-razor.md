---
target: src/Einsatzueberwachung.Server/Components/Pages/EinsatzImportExport.razor
total_score: 32
p0_count: 0
p1_count: 1
timestamp: 2026-06-16T11-53-54Z
slug: server-components-pages-einsatzimportexport-razor
---
# Critique — EinsatzImportExport (`/einsatz-import-export`)

**Register:** product (Import/Export & Merge-Wizard)
**As-found score:** 32/40 · P0 0 · P1 1 — **P1 behoben (Token-Migration Scoped-CSS)**

## Assessment A — Design review

Import/Export-Seite mit Tab-Umschalter, Modus-Auswahlkarten (integrieren / neuer Einsatz), mehrstufigem Merge-Wizard (Fortschritts-Stepper, Ziel-Auswahl, Kandidaten-Matching) und Import-Historie mit Rückgängig-Funktion. Sehr gute Zustandsabdeckung (Laden-Spinner, Leerzustände, Inline-Fehler je Merge, Disabled-States beim Revert) und Tastatur-Bedienbarkeit der Modus-Karten (`role=button`, `tabindex`, Enter/Space). Status mehrkanalig: Stepper kombiniert Nummer + Farbe + Label; resolved-Karten über farbigen Randstreifen + Badge.

### Befund (vor Fix)
Das Scoped-CSS nutzte durchgehend **rohe Bootstrap-Variablen** (`--bs-primary`, `--bs-success`, `--bs-secondary*`, `--bs-border-color`, `--bs-tertiary-bg`, `--bs-primary-bg-subtle`) plus **hardcodiertes `white`**. Da `forms.css` die `.btn-*`-Klassen auf `--theme-*`/`--signal-*` themt, aber die rohen `--bs-*` NICHT remappt sind (nur in der Bootstrap-Lib definiert), war die **aktive Stepper-Stufe Bootstrap-Blau (#0d6efd)** — inkonsistent zu den brand-roten `.btn-primary`-Buttons („Auswählen") derselben Seite, und nicht preset-/intensitäts-/dark-adaptiv.

### Heuristik-Scores (0–4)
| # | Heuristik | vorher | nachher |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | 4 |
| 2 | Realwelt-Bezug | 4 | 4 |
| 3 | Kontrolle & Freiheit | 4 | 4 |
| 4 | Konsistenz & Standards | 1 | 4 |
| 5 | Fehlervermeidung | 3 | 3 |
| 6 | Wiedererkennung | 3 | 4 |
| 7 | Flexibilität/Effizienz | 4 | 4 |
| 8 | Ästhetik/Minimalismus | 3 | 3 |
| 9 | Fehlererkennung/-behebung | 4 | 4 |
| 10 | Hilfe & Doku | 2 | 3 |

## Angewandte Fixes (CSS-only, `EinsatzImportExport.razor.css`)
- `.merge-step-circle` (idle): `--bs-secondary-bg/--bs-border-color/--bs-secondary-color` → `--ui-surface-2 / --ui-border / --ui-muted`.
- `.merge-step.active`: `--bs-primary` + `white` → `--theme-primary / --theme-primary-border / --theme-on-accent` (Label → `--theme-primary-text`). **Jetzt brand-konsistent mit `.btn-primary`.**
- `.merge-step.completed` + Connector: `--bs-success` + `white` → `--success-color / --success-border / --success-on-fill`.
- `.merge-item-card` Randstreifen: `--bs-border-color/--bs-success/--bs-primary/--bs-secondary` → `--ui-border / --success-color / --theme-primary / --ui-muted`.
- `.candidate-option:hover/.selected`: `--bs-tertiary-bg/--bs-primary-bg-subtle/--bs-primary` → `--ui-surface-2 / --theme-primary-soft / --theme-primary-border`.
- Neutralen Elevation-Shadow (`rgba(0,0,0,.12)`) belassen (Hover-Höhe, keine Markenfarbe).

## Residual / notiert
- **P3 — Markup-Utilities** `text-primary`/`border-primary`/`text-success`/`border-success` auf den Modus-Karten bleiben Bootstrap-Blau/-Grün (nicht brand). Reine Vor-Auswahl-Ansicht, nicht zeitgleich mit dem (nun brand-konformen) Stepper sichtbar → Markup-/Rebuild-Thema, dokumentiert.

## Re-Derivation
Nach Migration leiten alle Stati über `--theme-*`/`--success-*`/`--ui-*` ab → hält unter NRW + Ruhr + Dark + Intensitäten; aktive Stufe folgt der Marke statt Bootstrap-Blau.

**Score nach Fix:** ~37/40.

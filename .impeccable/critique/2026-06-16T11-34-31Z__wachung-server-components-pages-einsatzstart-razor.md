---
target: src/Einsatzueberwachung.Server/Components/Pages/EinsatzStart.razor
total_score: 36
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T11-34-31Z
slug: wachung-server-components-pages-einsatzstart-razor
---
# Critique — EinsatzStart (`/einsatz-start`)

**Register:** product (Lagezettel / Einsatz-Anlage)
**As-found score:** 36/40 · P0 0 · P1 0

## Assessment A — Design review

Dichter „Lagezettel", bewusst für 1366×768 ohne Scrollen gebaut. Szenario-Hero (Radio-Kacheln mit datengetriebenem Akzent), Einsatzort-Hero, Zwei-Spalten-Hauptgrid (Lagedaten | Führung), roter Start-CTA, Divera-Side-Sheet zum Befüllen. Durchgehend Token-getrieben mit `color-mix`-Ableitungen — vorbildlich. Mehrkanalige Zustände (Pflicht-Badge, is-missing-Ring, Pill-Icons, aria-checked). Kein Slop.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Pflicht-Marker, is-missing-Ring, Busy-Spinner, Erfolgs-/Fehler-Toast |
| 2 | Realwelt-Bezug | 4 | Domänenbegriffe, Szenario-Sprache, Melder/Stichwort |
| 3 | Kontrolle & Freiheit | 4 | Abbrechen, Trainer-Lage entfernen, Stepper, Zurück |
| 4 | Konsistenz & Standards | 3 | `#fff` auf Pill/Start-CTA-Fill; hartkodierte z-index; Bootstrap-Badges im Drawer |
| 5 | Fehlervermeidung | 4 | DataAnnotations, Clamp 1–50, „Einsatz bereits aktiv"-Guard |
| 6 | Wiedererkennung | 4 | Stammdaten-Dropdowns + Freitext, Icons+Labels |
| 7 | Flexibilität/Effizienz | 4 | Tastatur-Stepper, Divera-Import, Karten-Adresse-Auto-Sync |
| 8 | Ästhetik/Minimalismus | 4 | Ruhig, dicht, klare Sektionen |
| 9 | Fehlererkennung/-behebung | 3 | Fehler-Toast vorhanden; Feld-Fehlertexte sparsam |
| 10 | Hilfe & Doku | 2 | Auto-Sync-Tooltip; sonst wenig Inline-Hilfe (selbsterklärend) |

## Priorisierte Findings

- **P2 — `#fff` auf Fills (Konsistenz/Kontrast):** `.es-pill.is-active` und `.es-start-btn` setzten hartes `#fff` auf Marken-/Danger-Fill — identische Klasse wie der Ruhr-Kontrast-Bug; bricht bei hellem Custom-Primary. → **behoben:** `--theme-on-accent` (Pill primary), `--danger-on-fill` (Danger-Pill + Start-CTA).
- **P3 — Hartkodierte z-index 1040/1041:** → **behoben:** auf `--z-backdrop` / `--z-modal` gemappt.
- **P3 — Bootstrap-Badges im Divera-Drawer** (`bg-warning text-dark`, `bg-success/-danger/-secondary`): externe Alarmdaten in Sekundär-Sheet, lesbare BS-Kombi — belassen.

## Re-Derivation
Nach Fix folgen alle Fill-Textfarben dem Luminanz-System (NRW + Ruhr + Dark + alle Intensitäten); Drawer-Stapelung nutzt die semantische z-Skala.

**Erwartet nach Fix:** ~38/40 (Konsistenz 3→4).

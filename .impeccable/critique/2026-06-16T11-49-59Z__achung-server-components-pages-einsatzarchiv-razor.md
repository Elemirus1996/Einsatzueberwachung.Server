---
target: src/Einsatzueberwachung.Server/Components/Pages/EinsatzArchiv.razor
total_score: 36
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T11-49-59Z
slug: achung-server-components-pages-einsatzarchiv-razor
---
# Critique — EinsatzArchiv (`/einsatz-archiv`)

**Register:** product (Archiv & Auswertung)
**As-found score:** 36/40 · P0 0 · P1 0 — **Scoped-CSS token-clean; offene Punkte markup-/IA-seitig**

## Assessment A — Design review

Archivseite mit Filterleiste, Kennzahlen, Mini-Balkenchart (Verlauf monatlich/jährlich), Tabelle und Detail-Panel (Teams, Notizen, Suchgebiete, GPS-Tracks, Merge-Historie). Scoped-CSS klein und sauber token-getrieben (`--ui-tile-border`, `--ui-surface-2`, `--ui-text`). Vollständige Zustandsabdeckung: Laden, Leer, Filter-Leer (Trend), Status-Alert. Status mehrkanalig (Badges mit Label+Farbe: „Abgeschlossen"/„In Bearbeitung"/„Rückgängig gemacht").

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Lade-/Leer-/Status-Zustände, Treffer-Zähler |
| 2 | Realwelt-Bezug | 4 | Einsatz/Übung, Einsatzleiter, Ergebnis |
| 3 | Kontrolle & Freiheit | 3 | Löschen mit Confirm; aber neutral gestylt |
| 4 | Konsistenz & Standards | 3 | **Zwei Modal-Vokabulare** auf einer Seite |
| 5 | Fehlervermeidung | 3 | Confirm vorhanden; destruktiv schwach signalisiert |
| 6 | Wiedererkennung | 4 | Badges Label+Farbe, Farb-Swatches Suchgebiete |
| 7 | Flexibilität/Effizienz | 4 | Filter, Export/PDF/GPX, Merge-Revert |
| 8 | Ästhetik/Minimalismus | 4 | ruhige Tabellen, kompaktes Chart |
| 9 | Fehlererkennung/-behebung | 4 | klare Leer-/Filtertexte |
| 10 | Hilfe & Doku | 3 | title-Tooltips an Aktionsbuttons |

## Priorisierte Findings

- **P2 — Doppeltes Modal-Vokabular:** Track-Detail-Popup nutzt Bootstrap `.modal show d-block` + `.modal-backdrop` mit **inline `z-index: 2000/2001`** (umgeht die semantische Skala `--z-backdrop 1049` / `--z-modal 1055`); der Lösch-Dialog nutzt das custom `.modal-overlay`/`.modal-modern`-Muster. Vereinheitlichung + Token-z-index wäre richtig, ist aber ein Markup-/Layout-Umbau (Rebuild, Scroll-/Stacking-Risiko) → außerhalb des Token-Sweeps, dokumentiert.
- **P3 — Destruktiv schwach signalisiert:** „Löschen" in der Tabelle ist `btn-outline-secondary`, der Bestätigen-Button `btn-primary` (blau) statt danger. Markup-Entscheidung → notiert.
- **P3 — Chart-Balkenhöhe als `px` aus Prozentwert** (`height:@heightPct px`): funktioniert im 100px-Container, semantisch unsauber → notiert.

## Re-Derivation
Scoped-CSS ohne Hardcodes; Inline-Farben nur für dynamische Nutzerdaten (Suchgebiet-/Track-Farben) — legitim. Hält unter NRW/Ruhr/Dark/Intensitäten. Kein Token-Eingriff nötig.

**Score nach Durchsicht:** 36/40 (CSS sauber; Modal-Vereinheitlichung + z-index-Token als nächste, bewusste Markup-Aufgabe).

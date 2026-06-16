---
target: src/Einsatzueberwachung.Server/Components/Pages/TeamLogin.razor
total_score: 36
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T12-07-02Z
slug: berwachung-server-components-pages-teamlogin-razor
---
# Critique — TeamLogin (`/team/login`)

**Register:** product (Feld-Login, TeamMobileLayout, Smartphone)
**As-found score:** 36/40 · P0 0 · P1 0 — **Clean pass; keine Änderung nötig**

## Assessment A — Design review

Token-gestützter Team-Login für das Handy im Feld. Kein eigenes Scoped-CSS (Styles aus `TeamMobileLayout`/global). Große Touch-Ziele: `form-control-lg`, Team-Auswahl als `btn-outline-primary btn-lg w-100`. Status mehrkanalig: GPS-aktiv = `bi-broadcast text-success` **+ `title="GPS aktiv"`**; Team-Karten tragen Icon + Teamname + Hund + Hundeführer. Vollständige Zustände: ungültiger/abgelaufener Code (`alert-danger` + Text), aktiver Einsatz ohne Teams (`alert-info` + Text), Teamliste.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | aktiver Einsatzort, GPS-Indikator |
| 2 | Realwelt-Bezug | 4 | QR/Anmelde-Code, Team-Sprache |
| 3 | Kontrolle & Freiheit | 3 | Code erneut eingeben möglich |
| 4 | Konsistenz & Standards | 4 | Brand-Buttons, semantische Alerts |
| 5 | Fehlervermeidung | 4 | Code-Uppercase-Trim, klare Fehlermeldung |
| 6 | Wiedererkennung | 4 | Icon+Label je Team und Status |
| 7 | Flexibilität/Effizienz | 3 | QR-Scan oder Direkteingabe |
| 8 | Ästhetik/Minimalismus | 4 | ruhige Karten, klarer Fokus |
| 9 | Fehlererkennung/-behebung | 4 | eindeutige Ungültig-Meldung |
| 10 | Hilfe & Doku | 3 | Hinweistext zum QR-Code |

## Priorisierte Findings

- **Keine P0/P1.** Feldtaugliche Touch-Ziele, mehrkanaliger GPS-Status, vollständige Zustände.
- **P3 — „Weiter"-Primärbutton ohne `btn-lg`**, während Team-Auswahl `btn-lg` ist. Voll breit (`w-100`), daher tippbar; für Konsistenz/Daumenbedienung könnte er ebenfalls `btn-lg` sein. Belassen.

## Re-Derivation
Kein eigenes CSS; Brand-Buttons/Alerts erben Theme/Dark/Intensität über forms.css. GPS-Status mehrkanalig. Kein Token-Eingriff nötig.

**Score nach Durchsicht:** 36/40.

---
target: src/Einsatzueberwachung.Server/Components/Pages/DiveraStatus.razor
total_score: 37
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T12-02-48Z
slug: wachung-server-components-pages-diverastatus-razor
---
# Critique — DiveraStatus (`/divera`)

**Register:** product (Integrations-Status, sekundäre Datenfläche)
**As-found score:** 37/40 · P0 0 · P1 0 — **Clean pass; keine Änderung nötig**

## Assessment A — Design review

Statusseite für die Divera-24/7-Integration: Verbindungsstatus, aktive Alarme mit Priorität, Rückmelde-Zähler (geantwortet / 30 Min / 1 Std), Zeitstempel der letzten Aktualisierung. Kein eigenes Scoped-CSS. Status durchgehend **mehrkanalig**: Verbindung „Verbunden"/„Verbindungsfehler" trägt Icon (wifi/wifi-off) + Farbe + Label; Priority-Alarm über `exclamation-circle-fill` + `text-danger`. Hervorragende Zustandsabdeckung: Lade-Spinner, **Nicht-konfiguriert-Guard** (`!IsConfigured`), Verbunden, Fehler mit Meldung, Leerzustand „Keine aktiven Alarme".

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Verbindungs-Badge, Lade-Spinner, „zuletzt aktualisiert" |
| 2 | Realwelt-Bezug | 4 | Alarm/Rückmelde-Vokabular der Leitstelle |
| 3 | Kontrolle & Freiheit | 3 | Aktualisieren; rein lesende Ansicht |
| 4 | Konsistenz & Standards | 4 | Bootstrap-Semantik passend für echten Status |
| 5 | Fehlervermeidung | 4 | Guard bei fehlender Konfiguration |
| 6 | Wiedererkennung | 4 | Icon+Farbe+Label je Status |
| 7 | Flexibilität/Effizienz | 3 | Auto-/Manuell-Refresh |
| 8 | Ästhetik/Minimalismus | 4 | ruhige Karten, klare Badges |
| 9 | Fehlererkennung/-behebung | 4 | Fehlertext + Leer-/Guard-Zustände |
| 10 | Hilfe & Doku | 3 | Konfig-Hinweis bei fehlendem Key |

## Priorisierte Findings

- **Keine P0/P1.** Sekundäre Integrations-Datenfläche; Bootstrap-Statusfarben (grün=verbunden/geantwortet, rot=Fehler/Prio, amber=verzögert) sind hier **semantisch korrekt** und mehrkanalig.
- **P3 — Status-Badges nutzen Bootstrap `bg-success/-danger/-warning`** statt `--signal-*`. Da echte Status mit Icon+Label → ausreichend; Brand-Mapping optional, nicht erforderlich → belassen.

## Re-Derivation
Kein eigenes CSS; nur Layout-`max-width`-Inline. Erbt Theme/Dark über Bootstrap. Status mehrkanalig. Kein Token-Eingriff nötig.

**Score nach Durchsicht:** 37/40.

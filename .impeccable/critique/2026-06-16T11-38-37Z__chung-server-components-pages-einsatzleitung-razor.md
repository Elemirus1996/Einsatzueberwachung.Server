---
target: src/Einsatzueberwachung.Server/Components/Pages/EinsatzLeitung.razor
total_score: 36
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T11-38-37Z
slug: chung-server-components-pages-einsatzleitung-razor
---
# Critique — EinsatzLeitung (`/el`)

**Register:** product (Funktisch / Lagezentrum-Konsole, eigenes `ElLayout`)
**As-found score:** 36/40 · P0 0 · P1 0

## Assessment A — Design review

Stark art-direktierte „Funktisch"-Konsole: skeuomorphes Lagezentrum-Pult mit eigenem benanntem Vokabular (Funkstammbuch-Tape, Lagestreifen, Instrumentenplatte, Hardware-Taster). Vollständige lokale Palette `--funk-*` **mit Tag/Nacht-Varianten** (`[data-bs-theme="dark"]` + `.el-dark` + `prefers-color-scheme`) und **theme-abgeleiteten Akzenten** (`color-mix(--theme-primary/secondary …)`, Intensitäts-Glows). JetBrains-Mono-Typografie, BOS-Status-Vokabular (aktiv/Belastung 1–2/Pause). Das ist bewusste, exzellente Gestaltung — **kein Slop, nicht zu flachklopfen**. Idle-Hero + 4 Tabs (Übersicht/Vermisst/…), Push-Toasts, Wetter-Strip, Funktape.

### Heuristik-Scores (0–4)
| # | Heuristik | Score | Notiz |
|---|---|---|---|
| 1 | Sichtbarkeit Systemstatus | 4 | Lagestreifen, Live-Clock, Funktape, Push-Toasts, Belastungsstufen |
| 2 | Realwelt-Bezug | 4 | Konsequentes Funktisch/BOS-Vokabular |
| 3 | Kontrolle & Freiheit | 4 | Szenario wechseln, Tabs, Quick-Actions, Toast-Dismiss |
| 4 | Konsistenz & Standards | 3 | Vermissten/Szenario-Sub-Block fiel aus der Palette in `--bs-*`/Bootstrap-Blau |
| 5 | Fehlervermeidung | 3 | Busy-Guards je Team; viel Live-Mutation |
| 6 | Wiedererkennung | 4 | Aktiv/Bereit/Pause als Sektionen + Dot, mehrkanalig |
| 7 | Flexibilität/Effizienz | 4 | Dichte Konsole, Tastenkürzel-affin, Quick-Actions |
| 8 | Ästhetik/Minimalismus | 4 | Kohärente, mutige Art-Direction mit Disziplin |
| 9 | Fehlererkennung/-behebung | 3 | Alert-Banner im Vermisst-Tab, Offline-Badge |
| 10 | Hilfe & Doku | 3 | Selbsterklärend durch Vokabular/Idle-Hints |

## Priorisierte Findings

- **P2 — Bootstrap-Token-Leak im Vermissten/Szenario-Sub-Block (Konsistenz + Dark + NRW/Ruhr):** `--bs-body-bg/-color`, `--bs-border-color`, `--bs-tertiary-bg`, `--bs-primary-bg-subtle (rgba(13,110,253))`, `--bs-primary (#0d6efd)`, `--bs-secondary-color`, `--bs-danger`; zudem `rgba(0,0,0,.08)` auf der Tab-Nummer (im Dark-Mode unsichtbar). → **behoben:** auf `--funk-platte/-inschrift/-bezel/-platte-tief/-aktion/-aktion-glow/-on-anthrazit/-randnotiz` + `--belastung-2` umgestellt. Aktiver Tab nutzt jetzt die theme-abgeleitete Aktionsfarbe statt Bootstrap-Blau.
- **P3 — `#fff` auf belastung-2/funk-aktion/anthrazit-Tastern:** bewusste Hochkontrast-Badges auf dunklen, theme-/dunkel-verankerten Fills — kontrastsicher, art-direktiert → belassen.
- **P3 — `.el-badge-szenario` weiße Schrift auf datengetriebener Szenariofarbe:** Szenario-Palette ist fix gewählt → belassen.

## Re-Derivation
Nach Fix folgt auch der Vermissten/Szenario-Block der Funktisch-Palette → adaptiert Tag/Nacht und NRW/Ruhr. Status weiterhin mehrkanalig (Sektion + Dot + Label + Belastungsfarbe).

**Erwartet nach Fix:** ~38/40 (Konsistenz 3→4).

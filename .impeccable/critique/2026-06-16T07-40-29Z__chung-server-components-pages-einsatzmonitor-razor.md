---
target: EinsatzMonitor
total_score: 34
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T07-40-29Z
slug: chung-server-components-pages-einsatzmonitor-razor
---
## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3 | No global connection/last-sync indicator for SignalR |
| 2 | Match System / Real World | 4 | Fluent operational German, domain-true panels |
| 3 | User Control and Freedom | 3 | Reply Abbrechen + Esc added; no undo for team reset (has confirm) |
| 4 | Consistency and Standards | 3 | Filter unified; residual button-variant mix in headers |
| 5 | Error Prevention | 3 | Dog-conflict, collar gate, disabled empty submit, close confirm |
| 6 | Recognition Rather Than Recall | 4 | Shortcut legend popover + labeled icons + visible selection |
| 7 | Flexibility and Efficiency | 4 | Keyboard nav discoverable, layout persistence, popouts, quick-notes |
| 8 | Aesthetic and Minimalist Design | 4 | Panel titles lead; Teams header <=4 targets; disciplined disclosure |
| 9 | Error Recovery | 3 | Plain-German messages, generic |
| 10 | Help and Documentation | 3 | Contextual shortcut legend + teaching empty states |
| **Total** | | **34/40** | **Good — top of band** |

## Anti-Patterns Verdict
Not AI slop. Prior tell (muted all-caps panel titles) fixed — titles now lead in --ui-text 0.9rem. Teams filter went from 5 competing colors to one neutral segmented control with status dots (second channel) + labels + counts. Residual #fff mapped to --danger-on-fill / --theme-on-accent.
detect.mjs crashed again (ERR_MODULE_NOT_FOUND scripts/lib/impeccable-config.mjs) — bundled detector unavailable. Manual scan: no side-stripe borders, no gradient text, no glassmorphism. One uppercase label (shortcut-help-title) inside a single popover — defensible, not section-grammar.
No browser overlay — no dev server / no Vite entry for Blazor Server.

## Overall Impression
Moved from composed-but-whispering to composed-and-legible. Net +4 (30 -> 34), all from five targeted passes, no regressions. Gap to Excellent is small-grain consistency + status visibility, not structure.

## What's Working
1. Glance-hierarchy fixed: titles lead; filter is one quiet control with dot+label+count, dezent fallback included.
2. Accelerators discoverable: ? legend surfaces Strg+H/N/M/T without resting clutter.
3. Clean emergency exits: reply Abbrechen + Esc, consistent with modal/alert dismiss.

## Priority Issues
- [P3] Residual button-variant mix in panel headers (theme-accent-btn/btn-info/btn-outline-*/btn-link). Fix: one secondary shape, accent reserved for primary. -> /impeccable polish
- [P3] No connection/last-sync status for live SignalR surface. Fix: live/stale indicator from hub state. -> /impeccable harden
- [P3] Note-source chip row grows unbounded. Fix: cap visible chips or compact select >5 teams. -> /impeccable layout

## Persona Red Flags
- Alex (power user): well-served now; nit = no stale-connection signal.
- Einsatzleiter under load: panels locatable, filter dots survive glare/colorblindness; would benefit from live/stale badge.
- Jordan (first-timer): lands well, nothing regressed; ? help reduces recall load.

## Minor Observations
- shortcut-help-title uppercase label fine as one popover header; don't propagate.
- Weather panel: loading state but no error-retry.
- Segments have aria-pressed; dots aria-hidden — correct.

## Questions to Consider
- Should the header carry an always-visible Live/getrennt status?
- One canonical secondary-button shape for headers?

---
target: EinsatzKarte
total_score: 38
p0_count: 0
p1_count: 0
timestamp: 2026-06-16T10-02-25Z
slug: wachung-server-components-pages-einsatzkarte-razor
---
## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 4 | Tile-load skeleton + tile-error banner now present; per-collar age/OOB pulse intact. Only stretch: no always-on map-level "letzte GPS-Aktualisierung / Verbindung" |
| 2 | Match System / Real World | 4 | Fluent operational German; Suchgebiete/Halsband/UTM domain-true |
| 3 | User Control and Freedom | 4 | Draw cancel, polygon save/abort, delete confirm, collapsible sidebar, dismissible tile-error |
| 4 | Consistency and Standards | 4 | Print dialog now shares the floating-panel option vocabulary; scoped CSS + map.css both on `--ui-*`/`--theme-*`; hardcoded white/grey gone |
| 5 | Error Prevention | 3 | Team-zoom disabled w/o team + warning; delete confirm |
| 6 | Recognition Rather Than Recall | 4 | Tabs icon+label+count; control triggers show current value summary |
| 7 | Flexibility and Efficiency | 4 | Tile/grid layers, embed, print viewport-sync, GPX import, keyboard on cards |
| 8 | Aesthetic and Minimalist Design | 4 | Print dialog is now token-driven fieldset/option rows; numbered 1–4 wall gone |
| 9 | Error Recovery | 4 | OOB "Freigeben" actionable; tile-load failure now surfaces a dismissible banner with VPN hint |
| 10 | Help and Documentation | 3 | Polygon-edit hint overlay strong; button titles; no broader help |
| **Total** | | **38/40** | **Excellent** |

## Anti-Patterns Verdict
Not AI slop — the map-first 2-column layout (vertical icon-tab rail + floating control panels + map main) has a real operational voice. detect.mjs crashed again (ERR_MODULE_NOT_FOUND scripts/lib/impeccable-config.mjs) — same bundled-detector outage; manual scan only. No browser overlay (Blazor Server, no Vite entry).

Manual scan confirms the prior pass closed the off-token debt: print dialog markup carries no `text-white`/`bg-dark`/`border-secondary`; scoped `EinsatzKarte.razor.css` and shared `utilities.css` sidebar headers run on `--ui-*`/`--theme-*`; `map.css` sidebar header/empty/search-close and the `#3b82f6` focus fallback are tokenized; the embed toolbar's deliberate dark palette is centralized into named local vars with rationale. No side-stripe borders, no gradient text. The one `background: white` left lives in a `display:none` Leaflet draw toolbar that never renders. backdrop-filter blur stays confined to the floating map overlays — purposeful, not decorative.

## Overall Impression
The consistency debt the Monitor pass paid down is now paid here too. The print dialog reads correctly in light mode, the whole surface re-derives under dark mode / Ruhr preset, and the tile layer now has both a load skeleton and a failure affordance — the exact gaps the last critique named. The page moved from "strong everyday surface, light-only print path" to a coherent, token-clean whole.

## What's Working
1. **One display-settings vocabulary.** The print dialog now uses the same option-row pattern as the floating Anzeige/Kartentyp/Gitter panels instead of a parallel numbered form — recognized, not re-learned.
2. **Tile lifecycle is visible.** Skeleton "Karte wird geladen…" on init (reduced-motion aware) and a dismissible "Netzwerk/VPN prüfen" banner on `tileerror` — the right call for a VPN-only deployment where tiles can stall.
3. **Status still carries a second channel.** OOB → red badge + "!" glyph + alert-danger; collars carry name + [ID] + battery icon + age; tab counts numeric. Survives glare/colorblindness per DESIGN.md.

## Priority Issues
- **[P3] No always-on map-level GPS freshness indicator.** Per-collar age exists, but there's no single "letzte GPS-Aktualisierung / Verbindung" readout at the map level. For a live-tracking surface, a glanceable freshness/connection chip would close the last visibility gap. → /impeccable harden
- **[P3] Error Prevention and Help sit at 3.** No destructive-path gaps, but there's room: confirm the print "Vollständige Karte" path can't silently produce an empty export when no layers are toggled on, and consider a one-line legend/help affordance for first-time operators. → /impeccable harden

## Persona Red Flags
- **Einsatzleiter (light mode, daylight tablet):** the P1 is resolved — "Einsatzkarte drucken" now renders readable option rows in light mode; zoom/detail choices are legible outdoors.
- **Operator on Ruhr/dark preset:** sidebar header, empty state, and focus ring now follow the theme; no more white bar or non-brand blue against the token-clean panels.
- **Alex (power user):** well-served — keyboard on cards, embed mode, viewport-sync print, GPX import. Remaining nit: still no map-level live/stale GPS chip.

## Minor Observations
- Verify the three stacked floating `.map-control-panel` don't overlap the Drucken FAB on short viewports (CSS caps `max-height: calc(100% - 5rem)` + scroll — likely fine).
- The tile-error banner copy is good; confirm it auto-clears once a later tile loads successfully (not only on manual dismiss).
- Battery uses icon+color; confirm color isn't the sole low-battery cue at a glance.

## Questions to Consider
- Should the map carry an always-visible "letzte GPS-Aktualisierung / Verbindung" chip, the way the Monitor wants one?
- Should the tile-error banner retry automatically, or stay manual-dismiss only?
- Is there a first-run/empty-map state worth designing, or is the operational context enough that operators never see a cold start?

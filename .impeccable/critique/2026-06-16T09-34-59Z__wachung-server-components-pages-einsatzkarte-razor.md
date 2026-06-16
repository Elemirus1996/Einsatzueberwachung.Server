---
target: EinsatzKarte
total_score: 33
p0_count: 0
p1_count: 1
timestamp: 2026-06-16T09-34-59Z
slug: wachung-server-components-pages-einsatzkarte-razor
---
## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 3 | Per-collar age + OOB pulse present; no map-level live/stale or tile-load state |
| 2 | Match System / Real World | 4 | Fluent operational German; Suchgebiete/Halsband/UTM domain-true |
| 3 | User Control and Freedom | 4 | Draw cancel, polygon save/abort, delete confirm, collapsible sidebar |
| 4 | Consistency and Standards | 2 | Two control vocabularies for same settings; scoped CSS on --bs-* while map.css on --ui-*; hardcoded white/grey |
| 5 | Error Prevention | 3 | Team-zoom disabled w/o team + warning; delete confirm |
| 6 | Recognition Rather Than Recall | 4 | Tabs icon+label+count; control triggers show current value summary |
| 7 | Flexibility and Efficiency | 4 | Tile/grid layers, embed, print viewport-sync, GPX import, keyboard on cards |
| 8 | Aesthetic and Minimalist Design | 3 | Floating panels clean; print dialog is a dense numbered 1–4 wall |
| 9 | Error Recovery | 3 | OOB "Freigeben" actionable; no tile-load failure handling |
| 10 | Help and Documentation | 3 | Polygon-edit hint overlay strong; button titles |
| **Total** | | **33/40** | **Good** |

## Anti-Patterns Verdict
Not AI slop in structure — the map-first 2-column layout with a vertical icon-tab rail and floating glass control panels has a real operational voice, not a scaffold. detect.mjs crashed (ERR_MODULE_NOT_FOUND scripts/lib/impeccable-config.mjs) — same bundled-detector outage as the Monitor run; manual scan only. No browser overlay (Blazor Server, no Vite entry).

Manual scan: no side-stripe borders, no gradient text. backdrop-filter blur on the floating control panels is the one glass use — defensible as a map overlay, not decorative. The real tell is **off-token color**: scoped EinsatzKarte.razor.css runs on `--bs-*` Bootstrap tokens with hardcoded hex fallbacks (#dee2e6/#6c757d/#fff/#e9ecef); `.map-sidebar .sidebar-header { background: white }`, `.sidebar-empty { color #6c757d }`, `.search-message-close { color #0c5460 }`; and the print dialog assumes a dark modal (`bg-dark`/`text-white`/`border-secondary`). This is exactly the hardcoded-#fff / header-vocabulary class of issue the Monitor pass just cleaned up — the Karte still carries it.

## Overall Impression
The Karte's everyday surface (rail + floating Anzeige/Kartentyp/Gitter panels) is the strongest, most on-brand part — calm, glanceable, token-driven, with progressive disclosure done right. The biggest opportunity is consistency debt the Monitor already paid down: hardcoded light-only colors that break under dark mode/presets, and a print dialog that re-implements the same tile/grid/visibility choices in a second, dark-assuming control language.

## What's Working
1. **Floating control panels.** Collapsed triggers show the current value (`@MapContentSummary`, `@SelectedMapLayerLabel`, `@SelectedGridLayerLabel`) so state is recognized, not recalled; expand reveals options. Token-driven, blur is purposeful. This is the model the rest of the page should follow.
2. **Status carries a second channel.** OOB → red badge with "!" glyph + alert-danger + icon; collars carry name + [ID] + battery icon + age text; tab counts are numeric. Survives glare/colorblindness per DESIGN.md.
3. **Inline help where it's risky.** The polygon-edit overlay explains drag/insert/delete in plain German at the moment of editing — teaches without a manual.

## Priority Issues
- **[P1] Print dialog assumes a dark modal.** Labels use `text-white`/`bg-dark`/`border-secondary` while `.modal-content` is `--ui-surface` (light in light mode) → white-on-white, invisible. Also embeds a `<style>` block and inline `style="font-size"`. Fix: drive on `--ui-*`/Bootstrap semantic classes; drop `text-white`, move the inline style to scoped CSS. → /impeccable harden
- **[P2] Off-token color breaks theming.** Scoped CSS on `--bs-*` + hex fallbacks; `.sidebar-header { background: white }`, `.sidebar-empty`/`.search-message-close` hardcoded greys; embed toolbar baked #14181f/#e6dfcc. Under dark mode / Ruhr preset these don't re-derive. Fix: map to `--ui-*`/`--theme-*` per the Re-Derivation Rule (the Monitor fix). → /impeccable polish
- **[P2] Two control vocabularies for the same settings.** Floating `map-control-radio` panels and the print dialog's Bootstrap form-check 1–4 wall both choose tile/grid/visibility, differently. Fix: reuse the floating-panel vocabulary (or a shared component) in the dialog; collapse the numbered sections. → /impeccable distill
- **[P3] Muted-uppercase headers echo the old Monitor tell.** `vtab-label` is uppercase + tracked; `sidebar-content-header` is `text-muted small`. Vertical rail labels are defensible, but the muted small section headers repeat what the Monitor moved away from. Fix: lead headers in `--ui-text`, reserve uppercase for the rail only. → /impeccable typeset
- **[P3] No map tile load / failure state.** `#einsatzMap` mounts with no skeleton or tile-error affordance. Fix: skeleton on init, toast on tile failure. → /impeccable harden

## Persona Red Flags
- **Einsatzleiter (light mode, daylight tablet):** opens "Einsatzkarte drucken" and several labels are white-on-white — can't read zoom/detail options under the one preset most likely outdoors. The P1.
- **Operator on Ruhr/dark preset:** sidebar header renders a white bar and grey "empty" text that ignore the theme — small but it reads as unfinished next to the token-clean map panels.
- **Alex (power user):** well-served — keyboard on cards, embed mode, viewport-sync print, GPX import. Nit: no map-level live/stale GPS indicator.

## Minor Observations
- `.sidebar-card-item:focus-visible` falls back to `#3b82f6` (a non-brand blue) — use `--theme-primary`.
- Floating panel `max-height: calc(100% - 5rem)` with internal scroll is good; verify the three stacked panels don't overlap the Drucken FAB on short viewports.
- Battery uses icon+color (good); confirm the color alone isn't the only low-battery cue at a glance.

## Questions to Consider
- Should the print dialog reuse the floating panel's controls instead of a parallel form, so there's one display-settings vocabulary?
- Is the map missing an always-visible "letzte GPS-Aktualisierung / Verbindung" indicator the way the Monitor wants one?
- Could the vertical rail be the only uppercase on the page, with every other header leading in ink?

---
target: team cards of the Einsatzmonitor
total_score: 17
p0_count: 0
p1_count: 4
timestamp: 2026-11-06T13-23-18Z
slug: nsatzueberwachung-server-components-teamcard-razor
---
# Einsatzmonitor Team Cards — Design Critique

**Target:** `src/Einsatzueberwachung.Server/Components/TeamCard.razor`, as rendered in the Einsatzmonitor. Review covered the desktop monitor and a 390 px mobile viewport, plus the card markup and styles.

## Design Health Score

| # | Heuristic | Score | Key issue |
|---|-----------|------:|-----------|
| 1 | Visibility of System Status | 3/4 | Timer and written state are clear, but the long pause status can erase team identity on mobile. |
| 2 | Match Between System and Real World | 2/4 | Team, dog, handler and helpers are not presented as one unambiguous roster; team and dog names can repeat. |
| 3 | User Control and Freedom | 2/4 | Actions are present, but reset has no undo and can stop a running timer immediately. |
| 4 | Consistency and Standards | 2/4 | Card layout is repeated, but reset behavior differs from the EL app surface and actions are detached from the timer. |
| 5 | Error Prevention | 1/4 | Resetting a running team stops and zeros its timer without confirmation in this UI. |
| 6 | Recognition Rather Than Recall | 1/4 | Users must infer roles from icons and expand a card to see helpers. |
| 7 | Flexibility and Efficiency of Use | 2/4 | Drag ordering and direct actions help, but repeated scanning and expanding add friction. |
| 8 | Aesthetic and Minimalist Design | 1/4 | The 4 px status stripe, repeated names and separated control row compete with the operational identity. |
| 9 | Error Recovery | 2/4 | Recent timer events are available, but only inside the expanded card; there is no undo. |
| 10 | Help and Documentation | 1/4 | Icon controls rely on hover tooltips despite the field/touch context. |
| **Total** | | **17/40 — Poor** | **For the team-card surface: major UX and safety issues remain.** |

## Anti-Patterns Verdict

**LLM assessment:** This does not read as generic AI-generated UI. It has a credible operational vocabulary, useful state labels, and a purposeful dense layout. The problem is not the palette; it is that the card's structure prioritizes a large status display over reliable team identification and action. The 4 px colored left rail is also a direct violation of the design system's ban on side-stripe accents. The status already has a timer, text and color, so the rail is redundant decoration rather than necessary communication.

**Deterministic scan:** The CLI detector could not load: `detect.mjs` imports the missing `.claude/skills/impeccable/scripts/lib/impeccable-config.mjs`, so no source-scan JSON or total count is available. Browser preflight succeeded, `detect.js` loaded in the fresh **[Human]** tab, and the visual overlay ran. On the visible cards it marked the left status rails as **“side-tab accent border”** and the pause metadata as **“tiny body text.”** The pause-text finding is credible at 0.72 rem in an outdoor field-use product. No additional detector console messages were captured.

## Overall Impression

The cards tell me that a timer is running or paused, but they do not reliably tell me **which team** I am looking at or put the next action beside the state that action affects. At 390 px, “Pause abgeschlossen” consumes more width than the entire card header and collapses the team-identity column to zero. This is not a cosmetic edge case: it defeats the card’s primary job during a live operation. The reset behavior is more serious still: the icon can stop and zero an active timer without confirmation, while the EL app endpoint explicitly rejects that same action while a search is running.

## What’s Working

- The timer uses tabular numerals and is paired with explicit state text; users do not have to rely on green/orange/red alone.
- Search-area, note, GPS and phone context are available in the card without opening a separate page.
- The drag handle and card controls have 44 px targets, and reset/edit/expand controls have accessible names. The expanded view also preserves recent activity for after-the-fact review.

## Cognitive Load

**7 of 8 checklist items fail.** The zones do provide some chunking, but: identity competes with the timer; timer and controls are separated; five card actions are visible for a running team (Pause, Stopp, Reset, Bearbeiten, Erweitern), plus the reorder handle; helper names require recall/expansion; and the long pause string can remove the team name entirely. The main failure is extraneous load: users must repeatedly map the action row back to the timer above it and infer which name belongs to which role.

## Priority Issues

### [P1] Reset can silently stop and erase a running search

- **Why it matters:** The reset button is an icon in the normal action row. `RequestResetTeamAsync` asks for confirmation only during an unfinished required pause; otherwise it immediately calls `ResetTeamTimerAsync`. `Team.ResetTimer()` calls `StopTimer()`, sets elapsed time to zero, and clears pause state. That means the visible reset action can stop an active team and erase its elapsed time without a confirmation step. In contrast, the EL app endpoint rejects reset while the team is running.
- **Fix:** Make the card and API surfaces agree. At minimum, disable or omit reset while running and require stopping the search first; otherwise show a clear confirmation naming the consequences. Do not leave a high-impact action discoverable only by a tooltip.
- **Suggested command:** `/impeccable harden`

### [P1] Team identity disappears on mobile in the long-pause state

- **Why it matters:** At 390 px the first card measured 322 px wide, with a 293 px header and a 302 px timer column. The flex identity column fell to **0 px**, and the team-name element had `clientWidth: 0`. “Pause abgeschlossen” therefore wins the row and the team name is not visible; the remaining dog/handler line can be mistaken for the card identity.
- **Fix:** Preserve a non-zero identity column in every state. At narrow widths, stack the timer below the name or shorten the pause presentation to a compact value plus a secondary label. Never let a status phrase consume the team-name slot.
- **Suggested command:** `/impeccable adapt`

### [P1] The full personnel roster is hidden with secondary history

- **Why it matters:** The dog and handler are shown in the header, but `HelferNames` appears only inside the expand panel, alongside GPS details, completed searches and recent notes. The currently visible surface therefore omits who else is assigned until the user expands a card—and expansion makes the card much taller, pushing neighboring teams out of view.
- **Fix:** Keep the operational roster visible in a compact, stable identity block: dog, handler and assigned helpers (or a clear helper count with names immediately available). Reserve expansion for GPS coordinates and historical detail.
- **Suggested command:** `/impeccable layout`

### [P1] The timer and its controls are separated by a context zone

- **Why it matters:** The timer is in the top-right header; Start/Pause/Stop/Reset/Edit sit in the footer below context chips. In the mobile pause card, the timer ended at y=405 and the footer began at y=513—a 108 px gap. For a running card the gap was 35 px. The relationship between state and control changes with content, and a running card exposes five choices at once.
- **Fix:** Co-locate the state and its primary action. Keep the action that changes the displayed state adjacent to the timer; group lower-frequency edit/reset actions separately. Preserve Stop as an immediately recognizable operational control, but do not make users scan across the card to find it.
- **Suggested command:** `/impeccable layout`

### [P2] Identity is redundantly labeled and status uses a forbidden stripe

- **Why it matters:** When `TeamName` equals `DogName`, the same name is rendered twice—once as the team title and again beside a paw icon. The paw/person icons are doing the role-labeling work without visible words. The 4 px status stripe then adds another high-salience signal that repeats the timer/status badge; the overlay correctly flagged it. Pause metadata is also tiny enough to be flagged on the field-use surface.
- **Fix:** Deduplicate equal team/dog names, make roles explicit in the compact roster, replace the side stripe with a token-based non-stripe status cue, and increase the pause metadata to a legible secondary size.
- **Suggested command:** `/impeccable quieter`

## Persona Red Flags

- **Casey, distracted mobile user:** A long pause message can erase the team name at 390 px. The card then spends vertical space on the status while the complete crew and action controls are lower or hidden.
- **Riley, stress tester:** Tapping Reset on a running team stops the timer and zeros elapsed time in the monitor, while the EL app surface blocks that operation. This is a genuine cross-surface behavior mismatch, not just a visual concern.
- **Alex, power user:** The timer-to-footer scan and repeated expansion of cards slow down comparing teams. The active state presents five card actions plus a reorder handle without a clear hierarchy.

## Minor Observations

- The status text and timer are good foundations; do not remove the text labels when simplifying color.
- The 44 px hit areas are field-appropriate, but increasing target size does not solve the semantic ambiguity of the icon-only reset/edit controls.
- The expandable history is useful; the issue is that it shares a disclosure boundary with the current crew roster.

## Questions to Consider

- Should Reset be unavailable while a team is running, matching the EL app endpoint, or should it require an explicit stop-and-reset confirmation?
- Should every helper name stay visible on the card, or should the card show a compact roster summary with one-tap expansion for names?
- Which should dominate the card’s first glance: team identity, current status, or the next available action? Right now the pause string wins—even when that makes identity disappear.

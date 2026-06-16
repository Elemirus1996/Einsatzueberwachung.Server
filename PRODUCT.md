# Product

## Register

product

## Users

Two primary users, one operation seen at two densities:

- **Einsatzleiter (incident commander)** in the ELW / command post, working from a
  laptop or tablet. Coordinates a live search: starts and ends the operation, runs
  team timers, assigns GPS collars, reads radio traffic and notes, watches the map.
  Often under time pressure with several teams in the field at once.
- **Hundeführer (dog handler) in the field**, on a phone during an active search.
  Shares phone-GPS for the human track, reads their assignment, checks the map and
  their search area. Outdoors, frequently in poor light and on a small screen.

Secondary: trainers running practice scenarios (Trainer module) and Staffel admins
maintaining personnel, dogs, and drones (Stammdaten) between operations.

The job to be done: **keep a shared, accurate picture of a live search operation so
the right team gets attention at the right moment** — and lose nothing that was logged.

## Product Purpose

Einsatzüberwachung.Server is an incident-command system for canine search-and-rescue
teams (Suchhundestaffeln). It runs VPN-internally on a Linux server and gives a
Staffel one live operational picture: teams and timers, live GPS tracking of search
dogs and the handler's phone track, interactive and rubble (Trümmer) maps, radio
messages, notes, scenario-aware checklists, and PDF after-action reports — plus a
Divera 24/7 link and a passworded trainer area.

Success is operational, not aesthetic: the commander can read the whole operation's
state at a glance, act without friction, and trust that what happened was recorded.
The interface is a tool in a high-stakes moment, not a destination.

## Brand Personality

**Calm, professional, authoritative.** Control-room grade. The baseline is quiet and
composed; the UI earns trust by being predictable and legible, not by being loud.
Voice is plain operational German — direct, unambiguous, no marketing gloss. Emotion
to evoke: *I am in control and I can see everything that matters.* The interface only
raises its voice — color, motion, sound — when something genuinely needs attention,
and never cries wolf.

## Anti-references

- **Generic Bootstrap admin template** (AdminLTE-style): stock card grids, no
  identity, every screen the same shrugging dashboard. This product has a real
  operational voice; it should not read as a scaffold.
- **Playful / gamified consumer app**: mascots, confetti, toy-rounded everything,
  celebratory micro-delight. Inappropriate for an emergency operation.
- Anything that adds cognitive load or false alarm in a high-stakes moment.

References to move **toward**: operational monitoring dashboards (Grafana, Datadog,
Linear) for calm density and disciplined state color; tactical/mapping tools (ATAK,
dispatch/CAD) for map-first, glanceable markers.

## Design Principles

1. **Situational awareness first.** The commander must read the operation's whole
   state in one glance — which team, which dog, which timer needs attention — without
   reading paragraphs. Status is shape and position before it is prose.
2. **Quiet by default, loud only when it matters.** Calm control-room baseline.
   Escalation (saturated color, motion, audio) is a scarce resource reserved for
   genuine attention states, so that when the UI does shout, it is believed.
3. **Status never depends on color alone.** Every state carries a second channel —
   icon, shape, label, or position — so it survives red/green color blindness, glare,
   and a phone screen in daylight. The timer ramp (Grün→Orange→Rot) is the test case.
4. **One system, two densities.** The command-post desktop and the field phone are
   the same operation at different zoom levels. Consistency of vocabulary across them
   beats per-screen cleverness; a handler should recognize what the commander sees.
5. **Earned familiarity over novelty.** Standard affordances for standard tasks. The
   tool should disappear into the operation. Never reinvent a control for flavor when
   a known one works.

## Accessibility & Inclusion

- **Colorblind-safe status states (required).** Team, dog, and timer states must not
  rely on hue alone. Pair every signal color with an icon, shape, label, or position
  so red/green-deficient users read the same state. Audit the Grün→Orange→Rot timer
  ramp specifically.
- **Sunlight / outdoor readability (required).** The mobile field views must stay
  legible on a phone in daylight: strong contrast, no light-gray-on-tint body text,
  generous type and target sizes outdoors.
- Target **WCAG 2.1 AA** contrast across both light and dark themes (body ≥ 4.5:1,
  large text ≥ 3:1), including the existing intensity presets (dezent / ausgewogen /
  lebhaft).
- Honor `prefers-reduced-motion` for the blinking/pulsing alert states with a
  non-animated equivalent (e.g. a solid high-contrast treatment).

## Notes

- **UI language is German**; this strategic document is in English for tooling. Keep
  user-facing copy in plain operational German.
- The visual system is mature: a dynamic theme engine (presets + dezent/ausgewogen/
  lebhaft intensity), full dark/light mode, an extensive semantic token layer
  (`--ui-*`, `--signal-*`, `--theme-*`), system-font UI with JetBrains Mono for data,
  and emergency-services brand reds (`--theme-primary #A72920`, `--theme-tertiary
  #E3000F`). Run `/impeccable document` to capture this as DESIGN.md.

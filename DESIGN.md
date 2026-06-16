---
name: Einsatzüberwachung
description: Control-room interface for canine search-and-rescue incident command — operator-themable, intensity-tunable, dark/light.
colors:
  # ── Brand slot: operator-themable (default = NRW preset) ──
  primary: "#A72920"          # NRW deep red (default). Swapped per preset / custom theme.
  secondary: "#404040"        # NRW neutral graphite (default).
  primary-ruhr: "#005D9E"     # Ruhr preset — blue alternative.
  secondary-ruhr: "#FFED00"   # Ruhr preset — yellow alternative.
  on-accent: "#ffffff"        # Text on the filled brand color (#0f1720 under Ruhr).
  # ── Fixed signal anchors: constant across every preset ──
  signal-red: "#E3000F"       # tertiary — alarm / active-indicator anchor.
  signal-green: "#5BB969"     # quaternary — pause / idle anchor.
  # ── Status seeds (mixed with signal-red at runtime) ──
  status-warning: "#d97706"
  status-danger: "#e12431"
  status-info: "#6c7a8b"
  # ── Neutrals (light) ──
  bg: "#f5f7fb"
  surface: "#ffffff"
  surface-2: "#eef3fb"
  border: "#d5deea"
  ink: "#1f2937"
  muted: "#617083"
  heading: "#163253"
  link-blue: "#1f6fbe"
  sidebar-navy: "#0d1f3c"
  # ── Neutrals (dark) ──
  dark-bg: "#05070c"
  dark-surface: "#1a1d24"
  dark-surface-2: "#2a2d35"
  dark-border: "#323843"
  dark-ink: "#e8edf6"
  dark-muted: "#9aa4b5"
  dark-heading: "#4aa8ff"
typography:
  heading:
    fontFamily: "'Segoe UI', Tahoma, Geneva, Verdana, sans-serif"
    fontWeight: 700
    letterSpacing: "0.01em"
  body:
    fontFamily: "'Segoe UI', Tahoma, Geneva, Verdana, sans-serif"
    fontSize: "16px"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: "'Segoe UI', Tahoma, Geneva, Verdana, sans-serif"
    fontWeight: 600
    fontSize: "1rem"
  data:
    fontFamily: "'JetBrains Mono', ui-monospace, 'SF Mono', 'Cascadia Code', monospace"
    fontWeight: 500
rounded:
  control: "8px"
  button: "0.55rem"
  tile: "10px"
  card: "12px"
  modal: "20px"
  pill: "999px"
  theme-default: "8px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
  xxl: "32px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-accent}"
    rounded: "{rounded.button}"
    padding: "12px 24px"
    height: "44px"
  button-primary-hover:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-accent}"
  button-secondary:
    backgroundColor: "{colors.secondary}"
    textColor: "{colors.ink}"
    rounded: "{rounded.button}"
    padding: "12px 24px"
    height: "44px"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "12px 16px"
    height: "44px"
  card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.card}"
    padding: "24px"
  badge:
    rounded: "{rounded.pill}"
    padding: "0.35em 0.65em"
---

# Design System: Einsatzüberwachung

## 1. Overview

**Creative North Star: "Das Lagezentrum" (The Situation Room)**

This is a calm command surface. An Einsatzleiter in the ELW reads the whole operation
at a glance — which team, which dog, which timer needs attention — while the interface
stays composed until something genuinely demands a response. The same system folds down
to a Hundeführer's phone in daylight without losing its vocabulary. Design serves the
operation; it is a tool in a high-stakes moment, never a destination.

The system's identity is **not a fixed brand color** — it is the architecture beneath it.
Operators choose a theme preset (NRW deep red, Ruhr blue) or define a custom one, and the
entire surface re-derives from two seed colors (`--theme-primary`, `--theme-secondary`)
through `color-mix`. On top of that sits a **visual-intensity engine** with three presets —
`dezent` (zero noise: flat, no motion, 3px radii), `ausgewogen` (the balanced default), and
`lebhaft` (saturated fills, glows, 14px radii) — plus full light/dark theming. A screen
must look right across every combination. That re-derivation discipline is the design system.

It explicitly rejects the **generic Bootstrap admin template** (stock card grids, no voice)
and the **playful, gamified consumer app** (mascots, confetti, toy-rounded everything).
Nothing here should add cognitive load or cry wolf in a high-stakes moment.

**Key Characteristics:**
- **Token-derived, not hue-fixed** — operator-themable primary/secondary, fixed signal anchors.
- **Intensity-tunable** — dezent / ausgewogen / lebhaft gate color, motion, shadow, and radius.
- **Glanceable** — status is shape and position before it is prose.
- **Quiet by default** — saturated color, motion, and audio are scarce escalation, not decoration.
- **Field-ready** — ≥44px touch targets, legible outdoors, full dark mode.

## 2. Colors

A disciplined two-layer palette: an operator-themable **brand slot** over a fixed
**semantic signal vocabulary**, both resolved through a neutral system that flips for dark mode.

### Primary
- **Einsatz Brand Slot** (default `#A72920` NRW red): the operator-configurable seed. Carries
  primary actions, the current selection, and active brand emphasis. Swapped wholesale per
  preset (Ruhr `#005D9E` blue) or custom theme. Never assume it is red.
- **Brand Graphite** (default `#404040`): the secondary seed; neutral controls and secondary buttons.

### Secondary
- **Signal Red** (`#E3000F`, `--theme-tertiary`): a **fixed** anchor that does not change with the
  preset. Drives the accent gradient and the hue-mix for all alarm/danger signals.
- **Signal Green** (`#5BB969`, `--theme-quaternary`): the **fixed** pause/idle anchor.

### Tertiary
- **Status seeds** mixed at runtime with Signal Red: Warning `#d97706` (amber), Danger `#e12431`,
  Info `#6c7a8b` (slate), Success (a muted green). Exposed as `--success/-warning/-danger/-info`
  in text, border, fill, soft, and shadow strengths — all intensity-scaled.

### Neutral
- **Paper** (`#f5f7fb` light / `#05070c` dark, `--ui-bg`): the app background.
- **Surface** (`#ffffff` / `#1a1d24`, `--ui-surface`): cards, panels, content.
- **Cool Panel** (`#eef3fb` / `#2a2d35`, `--ui-surface-2`): the second neutral layer for
  sidebars, toolbars, segments — cooler than the content surface.
- **Hairline** (`#d5deea` / `#323843`, `--ui-border`): borders and dividers.
- **Ink** (`#1f2937` / `#e8edf6`, `--ui-text`): body text.
- **Muted Ink** (`#617083` / `#9aa4b5`, `--ui-muted`): secondary text — must still clear 4.5:1.
- **Heading Blue** (`#163253` / `#4aa8ff`, `--ui-heading-text`): headings and links read **blue**,
  deliberately separate from the brand slot so red stays reserved for action and alarm.
- **Sidebar Navy** (`#0d1f3c`): the command-rail base.

### Named Rules
**The Re-Derivation Rule.** Never hardcode a hex value in a component. Every color comes from a
token (`--theme-*`, `--ui-*`, `--signal-*`, `--success/-warning/-danger/-info-*`). If a new element
breaks when the operator switches preset, toggles dark mode, or changes intensity, it is wrong.

**The Reserved-Brand Rule.** The saturated brand primary appears on primary actions, current
selection, and active state only — never as decoration. Headings use Heading Blue, not the brand.

## 3. Typography

**Display / Heading Font:** 'Segoe UI' (with Tahoma, Geneva, Verdana fallback)
**Body Font:** 'Segoe UI' (same system stack)
**Label / Data Font:** 'JetBrains Mono' (with ui-monospace, SF Mono, Cascadia Code fallback)

**Character:** A neutral, ubiquitous system sans does the talking so the interface stays
invisible and fast; JetBrains Mono is the *funktisch* counterpoint — it carries operational
data, coordinates, timers, labels, and the Funkstammbuch, where tabular alignment and digit
clarity matter. One UI family, one data family, paired on a clear sans-vs-mono contrast axis.

### Hierarchy
- **Heading (h1)** (700, blue `--ui-heading-text`, letter-spacing 0.01em): page and section titles.
- **Title / Card header** (600): panel and card headers; fixed rem scale, not fluid.
- **Body** (400, 16px, line-height 1.6): default reading text; cap prose at 65–75ch.
- **Label** (600, 1rem): form labels and control captions.
- **Data / Mono** (JetBrains Mono 400–700): timers, coordinates, IDs, radio log — tabular data.

### Named Rules
**The Fixed-Scale Rule.** Type sizes are a fixed rem scale, never `clamp()`-fluid. Users work at a
consistent DPI across desktop and mounted tablets; a heading that shrinks in a sidebar reads worse.

**The Mono-For-Data Rule.** JetBrains Mono is for machine-readable values only (times, coords, IDs,
radio traffic). Never use it for prose, buttons, or UI labels that aren't data.

## 4. Elevation

**Flat by default, depth on demand.** Surfaces are flat at rest; elevation is a state response and
scales with the chosen intensity preset. In `dezent`, `--theme-card-shadow` is `none` and depth comes
from tonal layering (Paper → Cool Panel → Surface) alone. In `ausgewogen`, soft ambient shadows
appear. In `lebhaft`, shadows deepen and brand-tinted glows animate on active/alarm elements.

### Shadow Vocabulary
- **Card ambient** (`box-shadow: 0 2px 8px rgba(0,0,0,0.08–0.1)`): resting elevation for cards/panels
  (suppressed entirely in `dezent`).
- **Modal lift** (`box-shadow: 0 20px 60px rgba(0,0,0,0.3)`): dialogs above a `blur(4px)` backdrop.
- **Active glow** (`box-shadow: 0 6px 24px var(--theme-primary-shadow)`): `lebhaft`-only emphasis and
  the running/alarm pulse keyframes.

### Named Rules
**The Intensity-Gated Depth Rule.** Any new shadow or glow must be expressed through the intensity
tokens (`--theme-card-shadow`, `--theme-intensity-shadow`) so `dezent` stays truly flat and `lebhaft`
can amplify it. Don't bolt on a literal `box-shadow` that ignores the engine.

## 5. Components

Refined and restrained: calm, legible, generous touch targets, standard affordances done well. Every
interactive element is sized for the field first (WCAG ≥44×44px) and themed through tokens.

### Buttons
- **Shape:** rounded `0.55rem` (~9px); intensity can shift the themed radius 3px→14px.
- **Sizing:** min 44×44px, padding `12px 24px`, 1rem / weight 600. `.btn-lg` 56px / `16px 32px`;
  `.btn-sm` 44px / `8px 16px`.
- **Primary:** `color-mix(--theme-primary, surface)` fill scaled by `--theme-button-fill`, `--theme-on-accent`
  text, `--theme-primary-border`. **Hover/Focus:** full-strength primary fill + `0 0 0 0.2rem` brand focus ring.
- **Secondary:** secondary-seed fill with `--ui-text` (kept off the accent so heavily-tinted fills stay legible).
- **Signature — `.theme-accent-btn`:** a primary→signal-red gradient with a tinted shadow; flattens to a soft
  tint in `dezent`. The one deliberately expressive control.

### Chips / Badges
- **Style:** full pill (`border-radius: 999px`). Semantic variants (`text-bg-success/-info/-warning/-danger`)
  pull from the signal vocabulary and re-tune per intensity (faint in `dezent`, solid in `lebhaft`).

### Cards / Containers
- **Corner Style:** `12px` (modals `20px`, tiles `10px`).
- **Background:** `--ui-surface`; headers use the `--ui-card-header-bg` gradient + `--ui-card-header-text`.
- **Shadow Strategy:** Card ambient (see Elevation); flat in `dezent`.
- **Border:** 1px `--ui-border`. **Padding:** header `1.25rem`, body `1.5rem`.

### Inputs / Fields
- **Style:** `--ui-form-bg`, 1px `--ui-form-border`, `8px` radius, min-height 44px, padding `12px 16px`, 1.1rem.
- **Focus:** border shifts to `--ui-form-focus-border` (blue) with a `rgba(74,168,255,0.2)` glow ring.
- **Labels:** weight 600, 8px gap. Checkboxes/radios ≥24px for touch.

### Navigation
- **Sidebar:** 250px rail (collapses to 60px, icon-only), navy base, theme-aware text. **Active item:**
  inset 3px brand-tinted left bar + tinted background + weight 600. Section headers hide when collapsed.

### Signature — Status & Timer
Team timers ramp Grün → Orange → Rot with optional blink at critical. This is the system's most
load-bearing signal: it **must** carry a second channel (icon/label/position), never hue alone.

## 6. Do's and Don'ts

### Do:
- **Do** drive every color from tokens (`--theme-*`, `--ui-*`, `--signal-*`) so operator presets, custom
  themes, dark mode, and intensity all keep working. Test a new screen under NRW + Ruhr + dark + each intensity.
- **Do** pair every status color with a second channel — icon, shape, label, or position — so red/green-deficient
  users and a phone in sunlight read the same state. The Grün→Orange→Rot timer is the test case.
- **Do** keep the saturated brand primary scarce: actions, current selection, and alarm only.
- **Do** gate new shadows and motion behind the intensity engine so `dezent` stays flat and silent.
- **Do** keep touch targets ≥44px and body/muted text ≥4.5:1, especially in the mobile field views.
- **Do** use JetBrains Mono for data (times, coordinates, IDs, radio log) and the system sans for everything else.

### Don't:
- **Don't** hardcode hex values in components or bypass the token layers; it breaks theming and dark mode.
- **Don't** rely on color alone to convey team, dog, or timer state.
- **Don't** use the brand red (or any seed) as decoration, or color headings with it — headings are blue.
- **Don't** ship blinking/pulsing alerts without a `prefers-reduced-motion` equivalent that mirrors `dezent`.
- **Don't** reach for a modal as the first answer; exhaust inline and progressive disclosure first.
- **Don't** let it drift toward a generic Bootstrap admin template or a playful, gamified consumer app.

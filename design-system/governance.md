# Beaver Board Design System v1.0 — Governance

**Status:** Approved Baseline  
**Approved:** 2026-07-01  
**Owner:** Beaver Board Team

---

## Purpose

This document defines the rules, constraints, and processes for maintaining the Beaver Board visual identity and UI consistency across all surfaces.

---

## File Structure

```
design-system/
├── beaver-board-design-system.html   # Full interactive reference
├── tokens.css                        # CSS custom properties (importable)
├── tokens.json                       # Tokens as structured data
├── components.css                    # Reusable component styles
└── governance.md                     # This file

branding/beaver-board/
├── prompts/                          # Image generation prompts
├── beaver-icon.svg                   # Primary icon
├── original-branding.png             # Source mascot sheet
├── source-mascot-logos.png           # Logo variations
└── ...                               # Generated assets
```

---

## Token Hierarchy

Tokens follow a three-layer hierarchy:

1. **Primitive Tokens** (`--bb-*`) — Raw color values, never used directly in components
2. **Semantic Tokens** (`--color-*`) — Intent-based aliases referencing primitives
3. **Component Tokens** (`--button-*`, `--card-*`, `--agent-*`) — Component-specific bindings

### Rule: Never use raw hex values in components

```css
/* WRONG */
.my-component { background: #111113; }

/* CORRECT */
.my-component { background: var(--color-bg-panel); }
```

---

## Color Usage Rules

| Token | Usage | Never use for |
|-------|-------|---------------|
| `--color-action-primary` | CTAs, links, focus rings | Backgrounds, borders |
| `--color-status-success` | Tests passed, completed | Warnings, info |
| `--color-status-error` | Blocked, failed, destructive | Info, success |
| `--color-status-info` | Running, in-progress | Errors, warnings |
| `--color-status-warning` | Assigned, awaiting review | Errors, success |

### Agent Status Mapping

| Status | Badge Class | Color |
|--------|-------------|-------|
| Idle / Unassigned | `.badge-agent-idle` | Neutral gray |
| Assigned to Task | `.badge-agent-assigned` | Warning (amber) |
| Running OpenCode | `.badge-agent-running` | Info (blue) |
| Blocked | `.badge-agent-blocked` | Error (red) |
| Completed | `.badge-agent-completed` | Success (green) |

---

## Spacing Rules

- Use `var(--space-*)` for all spacing
- Base unit: 4px (`--space-1`)
- Never use arbitrary pixel values
- Card padding: `--space-4` (16px)
- Section gaps: `--space-8` to `--space-12`

---

## Typography Rules

- Body: `--font-sans` (Inter)
- Code/mono: `--font-mono` (JetBrains Mono)
- Never mix font families within a component
- Headings use `--line-height-heading` (1.2)
- Body text uses `--line-height-body` (1.5)

---

## Component Rules

### Kanban Cards

- Must use `.k-card` base class
- State variants: `.is-running`, `.is-blocked`, `.is-review`, `.is-done`
- Always include ticket ID badge (`.badge-default`)
- Always include agent assignment or status badge

### Buttons

- Primary: `.btn-primary` — one per view maximum
- Secondary: `.btn-secondary` — destructive actions, alternative choices
- Ghost: `.btn-ghost` — tertiary actions, navigation

### Badges

- Agent statuses must use semantic badge classes
- Never create custom status colors
- Evidence badges: use `.badge-outline` for no evidence, semantic badges for status

---

## Branding Rules

### Mascot Usage

- The beaver mascot may be used in illustrations and empty states
- Never use the mascot in active UI elements (buttons, cards)
- Keep mascot proportional — do not stretch or distort

### Logo Usage

- Primary logo: `beaver-icon.svg`
- Horizontal logo: for headers and footers
- Minimum clear space: height of the logo mark

### Color Palette

| Color | Hex | Usage |
|-------|-----|-------|
| Primary Orange | `#F97316` | Hard hat, accents, CTAs |
| Amber | `#F59E0B` | Secondary accents, warnings |
| Beaver Brown | `#8B4513` | Mascot fur, warm tones |
| Dark Brown | `#5D3A1A` | Shadows, depth |
| White | `#FFFFFF` | Teeth, eyes, highlights |

---

## PR Review Checklist (Design Gate)

Before merging any UI change:

- [ ] Uses `var(--color-bg-panel)` instead of raw hex `#111113`?
- [ ] Spacing uses `var(--space-*)` variables?
- [ ] New feature has Empty, Loading, and Error states?
- [ ] Agent statuses use semantic badge classes?
- [ ] Contrast ratio WCAG AA compliant on dark backgrounds?
- [ ] Drag-and-drop has keyboard alternative?
- [ ] Focus outline (`:focus-visible`) intact?
- [ ] No KittyClaw branding or visual identity?

---

## Modification Process

1. **Token changes** — Require design lead approval
2. **Component changes** — Must update both `components.css` and `beaver-board-design-system.html`
3. **New components** — Must include all state variants (empty, loading, error)
4. **Breaking changes** — Require version bump and migration guide

---

## Versioning

- **Major:** Breaking token or component API changes
- **Minor:** New components, new token additions
- **Patch:** Bug fixes, documentation updates

Current version: **1.0.0** (Approved Baseline)

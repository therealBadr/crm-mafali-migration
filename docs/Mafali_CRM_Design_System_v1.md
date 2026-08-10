# Mafali CRM — Design System v1

Decided 2026-08-07, through iterative mockups while designing the Fichier_Client rebuild.
"Navy and teal" — a dark, structural anchor color paired with a bright accent, chosen
explicitly for high visibility and readability over the earlier neutral/muted options.
Applies app-wide, not just to Fichier_Client.

## Palette

| Role | Value | Use |
|---|---|---|
| Navy (base) | `#0b2340` | Toolbar/header band background |
| Navy (text on light) | `#0b2340` | Header text on hover, high-emphasis text on light surfaces |
| Teal (accent) | `#14b8a6` | Primary buttons, sort/interactive affordances, selection accent |
| Teal (hover) | `#0f9a8a` | Primary button hover |
| Teal (active/pressed) | `#0c8072` | Primary button active |
| Light blue tint | `#f0f6fb` | Table header row background, subtle section backgrounds |
| Header text (rest) | `#4a6178` | Column header text, muted until interacted with |
| Row hover wash | `#f0faf8` | Table row hover background |
| Row selected wash | `#e0f5f1` | Table row selected background |
| Border (light) | `#dbe6f0` / `#eef3f8` | Table borders, dividers on light surfaces |
| Border (navy toolbar) | `#2a4568` | Outline-button borders on the navy toolbar |

**Status badges** (pill-shaped, `border-radius: 10px`, `font-size: 11px`, `padding: 2px 8px`):
| Status | Background | Text |
|---|---|---|
| Client / positive | `#d6f5ef` | `#0f6e56` |
| Prospect / neutral-warm | `#fdeacb` | `#854f0b` |

Extend this pattern (light tint bg + darker same-hue text) for any future status type —
don't invent a new badge style per screen.

## Typography

- Body/row text: default sans, `13px`, color `#1a2b3d` on light surfaces.
- Column headers: `10.5px`, **uppercase**, `letter-spacing: .05em`, `font-weight: 500`,
  color `#4a6178` at rest → `#0b2340` on hover. This is deliberate — headers must read as
  a distinct category of text from row content, not just "bigger/bolder."
- Toolbar title: `13px`, `font-weight: 500`, `#e8f1fb` on navy.

## Interactive states (non-negotiable — apply to every control, every screen)

- **Primary button** (filled teal): darkens on hover (`#0f9a8a`), darkens further + `scale(.97)`
  on active/press, visible focus ring (`box-shadow: 0 0 0 2px #0b2340, 0 0 0 4px #14b8a6`).
- **Secondary/outline button** (on navy toolbar): transparent bg + `#2a4568` border, `rgba(255,255,255,.08)`
  wash on hover, `.14` on active, same focus ring treatment as primary.
- **Disabled buttons**: `opacity: .35`, `cursor: not-allowed`, no hover/active reaction at all —
  used whenever an action requires a selection that doesn't exist yet (e.g. Modifier/Supprimer
  with no row selected), matching the legacy app's actual `TableSelect = -1 → RETOUR` behavior.
- **Table row hover**: light teal wash, distinct from —
- **Table row selected**: stronger teal wash + `inset 3px 0 0 #14b8a6` left-edge accent on the
  first cell. Hover and selected must never look the same — a user needs to tell "I'm pointing
  at this" from "this is the one I picked."
- **Column header hover**: text darkens, background darkens slightly, a sort icon
  (`ti-arrows-sort`, Tabler outline) fades in — only on hover, not shown at rest, to avoid
  cluttering the header while scanning data.
- **Transitions**: `~150ms ease` on color/background/border changes, `~100ms` on the button
  press `transform: scale()`. Nothing should snap instantly — this was an explicit requirement
  ("change in shade or color when user put cursor on the button"), not a nice-to-have.

## Layout pattern (per data screen)

Toolbar band (navy) with the screen title on the left and primary/secondary actions on the
right → white card body containing the data table → header row with the styling above →
data rows. This is the pattern every Fichier_X screen should follow, not just Clients.

## Still open

- **Component library**: leaning Mantine (discussed earlier, not yet formally re-confirmed
  after the full schema reset) — batteries-included DataTable/Modal/Notifications reduce
  how much of the above has to be hand-implemented per screen versus configured once.
- Dark-mode variant of this palette hasn't been discussed — out of scope unless requested.

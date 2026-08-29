# GPTDeskTop Premium UI — DESIGN CONTRACT

> **STATUS: LOCKED**  
> **AUTHORITY:** Approved GPTDeskTop premium reference design  
> **TARGET BRANCH:** `ui/premium-real-runtime-v1`  
> **RULE:** No worker, developer, agent, or reviewer may invent, substitute, simplify, or redesign the visual system without explicit owner approval.

## 1. Non-negotiable design rule

The approved premium design is the visual source of truth. Implementation must reproduce its information architecture, visual hierarchy, density, spacing, card structure, navigation pattern, control placement, status language, and semantic colors while wiring only real GPTDeskTop capabilities.

**Not allowed:**
- adding new product concepts just because they look good;
- changing left navigation order or top command hierarchy without approval;
- replacing cards with unrelated layouts;
- using a different theme, palette, typography system, icon language, or spacing system;
- hiding real controls behind decorative mock UI;
- creating a second runtime path only to make the UI match the design;
- marking a section complete because it looks correct while its controls are not wired;
- marking a section complete because backend logic exists while the approved visible surface is missing.

## 2. Approved screen language

The premium application shell is:
- deep navy/black desktop surface;
- persistent left navigation rail;
- compact top workspace/runtime command strip;
- dense operational dashboard made from bordered rounded cards;
- restrained blue accent for primary actions and active navigation;
- green only for healthy/active/success runtime state;
- amber for waiting/attention state;
- red for destructive/stop/error state;
- compact Segoe UI Variable typography;
- high information density with clear grouping and minimal wasted space;
- operational rather than decorative UI.

## 3. Locked palette

Use the canonical runtime palette already present in `src/GPTDeskTop/UI/FluentTheme.cs`:

| Token | RGB | Hex |
|---|---:|---|
| Background | 5, 14, 24 | `#050E18` |
| Surface | 9, 23, 38 | `#091726` |
| Surface Alt | 12, 29, 47 | `#0C1D2F` |
| Surface Raised | 7, 20, 34 | `#071422` |
| Surface Hover | 16, 40, 65 | `#102841` |
| Accent | 10, 113, 255 | `#0A71FF` |
| Accent Hover | 39, 130, 255 | `#2782FF` |
| Accent Subtle | 11, 42, 74 | `#0B2A4A` |
| Text | 235, 243, 255 | `#EBF3FF` |
| Muted | 135, 153, 179 | `#8799B3` |
| Border | 28, 48, 70 | `#1C3046` |
| Success | 52, 211, 153 | `#34D399` |
| Warning | 245, 158, 11 | `#F59E0B` |
| Danger | 248, 81, 96 | `#F85160` |
| Info | 56, 189, 248 | `#38BDF8` |

No alternate theme palette may be introduced for this release target.

## 4. Locked navigation order

The primary left rail must preserve this order:

1. Dashboard
2. Projects
3. Open Conversations
4. Saved Monitors
5. Recovery / Runtime Inspector
6. Development Messages
7. GitHub / Git Settings
8. Settings

The current premium shell already exposes these real application destinations. New navigation items require explicit approval.

## 5. Locked dashboard composition

The main dashboard must converge to the approved composition:

### Top command strip
- Workspace / Project selector or active context
- runtime/model/profile context only when backed by real state
- runtime status
- Start Monitor
- Open ChatGPT
- Stop
- Resume

### Primary operational cards
- Open Conversations
- Saved Monitors
- Recovery Overview
- Quick Actions
- Runtime Status
- Recent Activity
- Current Browser / Chrome state
- Guard Rails
- Projects

No fake rows or demo-only values in the production surface. Empty states must be real empty states.

## 6. Functional truth rule

A visible element is permitted only when one of the following is true:
1. it invokes an existing canonical GPTDeskTop action;
2. it displays real persisted/runtime state;
3. it is part of a planned section explicitly listed in `PREMIUM_UI_MASTER_PLAN.md` and is not presented as working before implementation.

The premium UI must remain a presentation layer over canonical runtime behavior. It must not fork monitoring, recovery, delivery, project, GitHub, or settings logic.

## 7. Visual acceptance rule

Every screen requires visual acceptance at the target Windows scale before its progress may reach 100%.

Minimum matrix:
- 1920×1080 @ 100%
- 1920×1080 @ 125%
- 2560×1440 @ 100%
- minimum supported application window

Acceptance requires:
- no clipped labels;
- no overlapping controls;
- no accidental scrollbars on the primary dashboard;
- no hidden primary actions;
- consistent card padding/borders/radii;
- consistent typography and semantic status colors;
- approved navigation and control hierarchy preserved.

## 8. Change-control gate

Any intentional design deviation must be documented before code changes with:
- proposed deviation;
- reason;
- affected screen(s);
- before/after screenshot;
- explicit owner approval.

Without that approval, reviewers must request changes.

## 9. Definition of design-complete

**Design completion = 100% only when:**
- all planned screens match the approved premium design language;
- all controls are connected to canonical real behavior;
- all visible state is real state;
- visual regression tests and runtime tests pass;
- screenshots prove layout at the acceptance matrix;
- release installer contains the accepted UI;
- no placeholder/mock-only feature remains visible.

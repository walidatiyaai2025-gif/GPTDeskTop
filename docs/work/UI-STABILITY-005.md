# UI-STABILITY-005 — Shared Layout Stability & Overflow Prevention

Status: **IN PROGRESS**  
Priority: **P0**  
Issue: **#177**  
Branch: `agent/ui-stability-005-layout-system`

## Objective
Create one reusable WinForms layout system that protects current and future GPTDeskTop screens from visible overflow, clipping, unstable resize behavior and arbitrary spacing/sizing drift.

## Architecture
The implementation extends the existing Fluent/ScreenExperience stack with two central layers:

- `LayoutTokens.cs` — shared spacing, control height, radius, pane minimum and responsive breakpoint tokens.
- `LayoutStability.cs` — presentation-only runtime hardening attached to every open Form and every dynamically added control.

No monitor worker, CDP, SQLite, recovery, delivery, scheduling, instance-handoff or release behavior is allowed in this task.

## P0 — Layout Stability / Overflow Prevention
- [x] Central 4/8/12/16/24/32 spacing scale.
- [x] Central control-height and responsive breakpoint tokens.
- [x] DPI scaling enforced on open forms.
- [x] Minimum usable window guard.
- [x] Dynamic child-control registration so late-created UI receives the same rules.
- [x] Ellipsis for constrained labels and buttons.
- [x] Automatic full-text tooltip for long/truncated labels/buttons.
- [x] Button action rows wrap instead of overflowing horizontally.
- [x] Long multiline text gets vertical scrolling rather than growing the window.
- [x] Read-only prose wraps; code/log surfaces keep intentional internal horizontal scrolling.
- [x] Split panes retain usable minimum regions during resize.
- [x] Tab pages own their page scrolling behavior.
- [x] Grid cells stay single-line with tooltips and resizable columns.
- [x] Regression tests lock the overflow/long-content architecture and presentation-only boundary.

## P1 — Consistency / Design System
- [x] New central layout tokens are reusable by all screens.
- [x] Shared control margins use the spacing scale where controls previously had no explicit margin.
- [x] Inputs and buttons receive shared minimum heights.
- [ ] Follow-up screen audit: replace remaining justified/unjustified per-screen magic numbers with tokens where changing them is behavior-safe.
- [ ] Follow-up typography token extraction from `FluentTheme` so layout and type tokens live under one documented design-system contract.

## P2 — Visual Polish / Micro-interactions
- [ ] Final visual pass after exact-head CI is green.
- [ ] Validate loading/error/status placeholders for zero layout jumping on every screen.
- [ ] Keep animations limited to lightweight state transitions; no animation is added in this P0 implementation.

## QA Matrix
Required manual/Windows visual QA after CI build:

- 1280×720
- 1366×768
- 1920×1080
- 2560×1440
- manual resize down to the supported minimum
- DPI 100%, 125%, 150%
- 10,000-character message/body text
- very long URL
- very long file name
- very long error message
- very long code/log line
- very long model name

## Definition of Done
- no visible overflow or control overlap in supported screens
- resize remains usable and predictable
- long text is wrapped or ellipsized with a way to inspect the full value
- horizontal scroll exists only for intentionally unwrapped code/log/grid content
- spacing and minimum control sizing come from reusable shared tokens
- changes are documented here and on Issue #177
- exact final branch head passes the established GitHub Actions validation gates

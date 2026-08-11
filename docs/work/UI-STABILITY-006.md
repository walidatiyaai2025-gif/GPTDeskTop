# UI-STABILITY-006 — Deterministic Responsive Presentation Ownership

Status: **IMPLEMENTED / CI PENDING**  
Priority: **P0**  
Issue: **#191**  
Branch: `agent/ui-stability-006-responsive-ownership`

## Problem
UI-STABILITY-005 introduced a reusable generic responsive fallback while `SecondaryScreenExperience` already owns responsive geometry for MainForm, SettingsForm and MonitorSettingsForm. Both layers could write `TabControl.Padding` and `FlowLayoutPanel.WrapContents` during resize using different compact thresholds. That leaves the final UI dependent on event-handler ordering in overlap ranges such as 820–860 logical pixels.

## Implementation
- `LayoutStability` still applies one-time generic hardening to every form: long-text tooltips, minimum sizing, scrolling safeguards, grid behavior and split bounds.
- Generic `ApplyResponsiveState` now exits for forms with a specialized responsive owner.
- Specialized responsive ownership is explicit for `MainForm`, `SettingsForm` and `MonitorSettingsForm`.
- Those forms continue to receive their existing `SecondaryScreenExperience` responsive rules without a competing fallback writer.
- Generic fallback responsive behavior remains unchanged for forms without a specialized owner.

## Regression coverage
`LayoutStabilityRegressionTests` now locks:
- the specialized-owner early-exit contract,
- MainForm / SettingsForm / MonitorSettingsForm ownership,
- the existing specialized 820px and 800px compact thresholds,
- specialized tab padding and action-row wrapping,
- existing UI-POLISH-006 single-row command behavior,
- presentation-only architecture boundaries.

## Compatibility
No monitor worker, Chrome/CDP, SQLite, recovery, conversation identity, development-task scheduling, delivery or release semantics are changed.

## Definition of Done
- specialized forms have one responsive geometry owner
- generic layout hardening remains active without competitive responsive mutation
- generic fallback behavior remains available for unowned screens
- exact final PR head passes all eight established GitHub Actions workflows
- PR merges to `main` and Issue #191 closes Completed

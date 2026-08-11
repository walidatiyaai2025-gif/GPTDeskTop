# UI-POLISH-001 — Professional dashboard polish

## Status
IN PROGRESS

## Tracking
- GitHub issue: #159
- Branch: `agent/ui-polish-001-dashboard`
- Baseline main: `03ff8076e42d3e22ed76af3924bacc7184bf0fa8`

## Goal
Raise the visual quality of GPTDeskTop's existing Fluent/WinUI-inspired WinForms interface while preserving all runtime behavior.

## Scope
- Modernize the neutral/accent palette for stronger contrast and hierarchy.
- Improve shared typography for body, section, and action controls.
- Upgrade buttons with consistent hover/pressed states, spacing, weight, and rounded geometry.
- Upgrade bordered panels into rounded Fluent-style cards through the shared theme layer.
- Improve DataGridView density, row/header typography, selection treatment, and separators without changing selection semantics.
- Improve read-only/editable input distinction.
- Restyle context menus so they visually belong to the application.
- Keep runtime status colors, monitor logic, recovery behavior, persistence, and Chrome/CDP behavior unchanged.

## Implementation notes
The first implementation intentionally stays centralized in `UI/FluentTheme.cs`. MainForm already builds its dashboard from shared theme primitives and applies `FluentTheme.Apply(this)`, so this approach improves the header, section cards, selected-monitor card, action buttons, grids, inputs, tabs, and context menus without duplicating styling or touching operational event handlers.

## Regression controls
- Do not override grid `MultiSelect`, selection logic, or bindings from form-level configuration.
- Do not change monitor commands, event wiring, persisted settings, splitter behavior, timers, recovery, or Chrome lifecycle.
- Keep existing primary/danger button intent; only presentation changes.

## Validation
Pending GitHub Actions on the pull request because the execution environment cannot resolve github.com for a local clone/build. CI is the authoritative compile/runtime validation for this change.

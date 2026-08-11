# UI-STABILITY-005 — Shared Layout Stability & Overflow Prevention

Status: **IMPLEMENTED / CI PENDING**  
Priority: **P0**  
Issue: **#177**  
Branch: `agent/ui-stability-005-reconcile`

## Objective
Create one reusable WinForms layout-stability layer that protects current and future GPTDeskTop screens from visible overflow, clipping, unstable resize behavior and arbitrary spacing/sizing drift, while preserving the verified UI-POLISH-006 main-window action sizing and dock-order fix.

## Reconciliation note
The original implementation on PR #179 was based on an older `main`. This branch reimplements the same sprint from the current `main` baseline so newer UI, monitoring and performance work is preserved. The new implementation is intentionally incremental: open forms are traversed once, late-created controls are handled by `ControlAdded`, and responsive work runs on resize/DPI changes rather than repeatedly walking the full tree on every idle cycle.

## Architecture
- `LayoutTokens.cs` — shared logical-pixel spacing, control height, pane minimum and responsive breakpoint tokens.
- `LayoutStability.cs` — presentation-only WinForms hardening for constrained text, long-value tooltips, compact action wrapping, scrolling ownership, split-pane bounds, grids and read-only prose/log surfaces.
- `LayoutStabilityRegressionTests.cs` — source/runtime contracts for incremental registration, overflow handling, DPI scaling, compatibility with UI-POLISH-006 and the presentation-only boundary.

No monitor worker, Chrome/CDP transport, SQLite persistence, recovery, development-task scheduling, conversation identity, release publishing or delivery semantics are changed.

## P0 delivered
- [x] Central 4/8/12/16/24/32 spacing scale.
- [x] Shared logical control heights and responsive breakpoints.
- [x] DPI-aware runtime scaling through `DeviceDpi`.
- [x] Minimum usable size guard for sizable forms only.
- [x] One-time form traversal with dynamic `ControlAdded` registration.
- [x] Resize and DPI-change responsive refresh without per-idle tree traversal.
- [x] Ellipsis safety for constrained labels and genuinely clipped fixed-width buttons.
- [x] Automatic full-text tooltips for long/truncated labels and buttons.
- [x] Compact action-row wrapping where safe.
- [x] Explicit preservation of the Development Plan single-row command strip from UI-POLISH-006.
- [x] Vertical scrolling for long multiline text and tab-page content.
- [x] Intentional horizontal scrolling retained for code/log surfaces.
- [x] Split-pane minimum bounds during resize.
- [x] Grid tooltips, single-line cells and resizable columns.
- [x] Regression coverage locking architecture and compatibility boundaries.

## QA matrix
The established Windows CI gate set must pass on the exact final head. Visual acceptance targets remain:
- 1280x720, 1366x768, 1920x1080 and 2560x1440
- manual resize down to the supported minimum
- 100%, 125% and 150% DPI
- long URL/file/error/model values
- 10,000-character prose/message text
- long code/log lines

## Definition of Done
- no visible overflow or control overlap in supported screens
- current UI-POLISH-006 action labels remain fully readable
- long text is wrapped or ellipsized with a way to inspect the full value
- horizontal scroll exists only for intentionally unwrapped code/log/grid content
- reusable layout tokens are available for future screens
- exact final branch head passes the established GitHub Actions validation gates
- PR is merged to `main` and Issue #177 is closed Completed

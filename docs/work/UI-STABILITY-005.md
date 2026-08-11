# UI-STABILITY-005 — Shared Layout Stability & Overflow Prevention

Status: **DONE / VERIFIED / MERGED**  
Priority: **P0**  
Issue: **#177 — Closed / Completed**  
Superseded PR: **#179**  
Final PR: **#190**  
Verified PR head: `5db647dc958f366ab7d91587651ffbbca50afafd`  
Squash merge to `main`: `203b41c714f2dadbb9bc0e491ab9e2774c140587`

## Objective
Create one reusable WinForms layout-stability layer that protects current and future GPTDeskTop screens from visible overflow, clipping, unstable resize behavior and arbitrary spacing/sizing drift, while preserving the verified UI-POLISH-006 main-window action sizing and dock-order fix.

## Reconciliation result
The original implementation on PR #179 was based on an older `main` and was closed as superseded. PR #190 reimplemented the sprint from the current main baseline so newer UI, monitoring and performance work was preserved. The final implementation is incremental: open forms are traversed once, late-created controls are handled by `ControlAdded`, and responsive work runs on resize/DPI changes rather than repeatedly walking the full tree on every idle cycle.

## Architecture
- `LayoutTokens.cs` — shared logical-pixel spacing, control height, pane minimum and responsive breakpoint tokens.
- `LayoutStability.cs` — presentation-only WinForms hardening for constrained text, long-value tooltips, compact action wrapping, scrolling ownership, split-pane bounds, grids and read-only prose/log surfaces.
- `LayoutStabilityRegressionTests.cs` — source/runtime contracts for incremental registration, overflow handling, DPI scaling, compatibility with UI-POLISH-006 and the presentation-only boundary.

No monitor worker, Chrome/CDP transport, SQLite persistence, recovery, development-task scheduling, conversation identity, release publishing or delivery semantics were changed.

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

## Verification receipts
All eight established GitHub Actions workflows passed on exact final head `5db647dc958f366ab7d91587651ffbbca50afafd`:

- Build GPTDeskTop #609 — Success
- QA Release x64 #397 — Success
- QA Hidden Chrome CDP #379 — Success
- QA Passive Chat Wait #373 — Success
- QA Crash Process Recovery #387 — Success
- Development Delivery Receipts #487 — Success
- Development Task Recovery #483 — Success
- Development Message Reload #314 — Success

The main Build workflow completed runtime automation tests, application build, setup build, helper build and rotation-safety validation successfully. PR #190 was squash-merged to `main` as `203b41c714f2dadbb9bc0e491ab9e2774c140587`, and Issue #177 closed Completed.

## Visual QA matrix retained
- 1280x720, 1366x768, 1920x1080 and 2560x1440
- manual resize down to the supported minimum
- 100%, 125% and 150% DPI
- long URL/file/error/model values
- 10,000-character prose/message text
- long code/log lines

## Definition of Done
- [x] no architecture-level overflow hardening gaps in the shared UI layer
- [x] current UI-POLISH-006 action labels remain protected from the generic wrapping layer
- [x] long text can be wrapped or ellipsized with a full-value tooltip
- [x] horizontal scroll is retained only for intentionally unwrapped code/log/grid content
- [x] reusable layout tokens are available for future screens
- [x] exact final branch head passed the established GitHub Actions gate set
- [x] PR merged to `main`
- [x] Issue #177 closed Completed

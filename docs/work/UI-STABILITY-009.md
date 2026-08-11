# UI-STABILITY-009 — DPI-Aware Expandable Workspace Heights

Status: **DONE / VERIFIED / MERGED**  
Priority: **P0**  
Issue: **#197 — Closed / Completed**  
PR: **#198**  
Verified PR head: `02bbc472a90c6dda536d520f83baec1ddf2371c2`  
Squash merge to `main`: `4c74c87b3139390f77355cbe8bb7ab90521f6c79`

## Problem
Development Plan, Runtime Health and Stored History use logical fixed heights for expanded/collapsed states. WinForms can DPI-scale their initial layout, but toggling later writes the legacy raw height again. On 125%/150% DPI that can make the outer control smaller than its scaled child layout and reintroduce clipping/compression.

## Delivered
- Added `ExpandableWorkspaceLayout`, an incremental presentation-only DPI guard.
- Registers each open form/control tree once and handles late controls via `ControlAdded`.
- Tracks Development Plan (72/178 logical px), Runtime Health (62/188) and Stored History (56/330).
- Uses active `DeviceDpi` whenever current expanded/collapsed height is applied.
- Corrects both `Height` and collapsed `MinimumSize.Height`.
- Reapplies on `SizeChanged` and `DpiChangedAfterParent`, so a legacy raw toggle assignment is corrected synchronously and a monitor-DPI transition preserves the current state.
- Does not read or mutate monitor/runtime/database/development-task state beyond each control's public `IsExpanded` presentation state.

## Regression coverage
`ExpandableWorkspaceDpiRegressionTests` locks:
- active `DeviceDpi` scaling,
- all three logical height profiles,
- height/minimum correction,
- DPI/size event-driven refresh,
- incremental registration,
- presentation-only dependency boundary.

## Verification receipts
All eight established GitHub Actions workflows passed on exact final head `02bbc472a90c6dda536d520f83baec1ddf2371c2`:

- Build GPTDeskTop #618 — Success
- QA Release x64 #406 — Success
- QA Hidden Chrome CDP #388 — Success
- QA Passive Chat Wait #382 — Success
- QA Crash Process Recovery #396 — Success
- Development Delivery Receipts #496 — Success
- Development Task Recovery #492 — Success
- Development Message Reload #323 — Success

## Definition of Done
- [x] all three expandable dashboard sections keep DPI-scaled current heights after toggle and DPI changes
- [x] no business/runtime/persistence behavior changes
- [x] exact final PR head passed all eight established GitHub Actions workflows
- [x] PR merged to `main` and Issue #197 closed Completed

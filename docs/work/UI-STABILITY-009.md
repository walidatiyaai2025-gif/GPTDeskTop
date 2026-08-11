# UI-STABILITY-009 — DPI-Aware Expandable Workspace Heights

Status: **IMPLEMENTED / CI PENDING**  
Priority: **P0**  
Issue: **#197**  
Branch: `agent/ui-stability-009-dpi-heights`

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

## Definition of Done
- all three expandable dashboard sections keep DPI-scaled current heights after toggle and DPI changes
- no business/runtime/persistence behavior changes
- exact final PR head passes all eight established GitHub Actions workflows
- PR merges to `main` and Issue #197 closes Completed

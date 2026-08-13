# UI-HEALTH-001 — Saved Monitor live health rows

Status: IMPLEMENTED / pending CI and stable release validation

## Goal

Make the **Saved Monitors** grid an immediate operator health board:

- A row is green only when the monitor worker is running, its saved conversation is present, and the ChatGPT page can be read without a current rendered error.
- Every non-healthy monitor is red.
- Every red row shows a human-readable **Reason** in the grid.
- Normal ChatGPT generation or a slow response remains healthy/green; elapsed time is not a failure signal.

## Implementation

- Added `SavedMonitorHealthPresentation` for deterministic health/reason classification.
- Added `SavedMonitorHealthGridExperience` as a continuous, bounded UI health reconciler.
- Added a `Reason` column to Saved Monitors.
- Applies green/red background and foreground to the complete row, including the selected-row colors.
- Verifies Chrome target presence and live ChatGPT state for running monitors on a 2.5-second cadence.
- Uses single-flight scans and an 8-second safety bound so health checks cannot overlap indefinitely.
- Keeps recent monitor startup/runtime failure activity so a stopped worker can show the concrete failure reason instead of only `Stopped`.
- Clears stale failure context when a healthy start/recovery transition is observed.
- Stops and disposes the health timer before application shutdown.

## Failure reasons covered

- monitor disabled in settings;
- invalid saved conversation URL;
- duplicate conversation ownership;
- Chrome/CDP unavailable;
- monitor worker stopped;
- saved conversation tab not open / not available;
- ChatGPT page health probe failure;
- explicit ChatGPT rendered error / recovery state;
- recent startup/runtime failure reason emitted by the monitor service.

## Verification

Regression coverage is in:

- `SavedMonitorHealthPresentationTests`
- `SavedMonitorHealthGridRegressionTests`

Release acceptance requires the normal GPTDeskTop stable CI gates to pass for the same source commit before merging/publishing `Last release/GPTDeskTop.exe`.

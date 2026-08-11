# UI-STABILITY-008 — Main Dashboard Responsive Ownership

Status: **DONE / VERIFIED / MERGED**  
Priority: **P0**  
Issue: **#195 — Closed / Completed**  
PR: **#196**  
Verified PR head: `547629f72c337493b0b4c77b9c7d157d4f777d66`  
Squash merge to `main`: `dd10f9a4535173bbe924e40972d7e010f5ce3cf5`

## Problem
`MainDashboardExperience` still performed a full descendant presentation pass on every `Application.Idle`. Its responsive pass also rewrote every descendant button FlowLayoutPanel, crossing ownership boundaries with specialized operational controls. The Development Plan command row was the clearest conflict: its specialized rule requires a single row, while the main-dashboard fallback could turn wrapping back on.

## Delivered
- Main dashboard presentation enhancement is now one-time per MainForm.
- Application.Idle remains only a lightweight open-form discovery mechanism.
- MainForm resize still updates dashboard-shell responsive margins.
- Responsive mutation is scoped to BROWSER / MONITOR / RUNTIME / APP action groups only.
- Buttons inside one dashboard action group remain single-row.
- The outer MainForm toolbar retains responsibility for wrapping whole action groups.
- Development Plan, Runtime Health, History and Support Diagnostics retain their own responsive geometry.
- Existing accessibility, tooltips, metric colors, rounded surfaces, grid styling and activity presentation are preserved.
- The pre-merge stale source-contract test was updated to assert the new scoped ownership rule instead of the removed generic `flow.WrapContents` behavior.

## Verification receipts
All eight established GitHub Actions workflows passed on exact final head `547629f72c337493b0b4c77b9c7d157d4f777d66`:

- Build GPTDeskTop #616 — Success
- QA Release x64 #404 — Success
- QA Hidden Chrome CDP #386 — Success
- QA Passive Chat Wait #380 — Success
- QA Crash Process Recovery #394 — Success
- Development Delivery Receipts #494 — Success
- Development Task Recovery #490 — Success
- Development Message Reload #321 — Success

## Compatibility
No monitor worker, Chrome/CDP, SQLite, recovery, conversation identity, development-task delivery or release behavior changed.

## Definition of Done
- [x] idle discovery no longer reapplies the full dashboard presentation tree
- [x] MainDashboardExperience cannot re-enable wrapping on Development Plan controls
- [x] dashboard-shell command groups remain responsive
- [x] exact final PR head passed all eight established GitHub Actions workflows
- [x] PR merged to `main` and Issue #195 closed Completed

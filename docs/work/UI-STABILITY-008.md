# UI-STABILITY-008 — Main Dashboard Responsive Ownership

Status: **IMPLEMENTED / CI PENDING**  
Priority: **P0**  
Issue: **#195**  
Branch: `agent/ui-stability-008-dashboard-ownership`

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

## Compatibility
No monitor worker, Chrome/CDP, SQLite, recovery, conversation identity, development-task delivery or release behavior changes.

## Definition of Done
- idle discovery no longer reapplies the full dashboard presentation tree
- MainDashboardExperience cannot re-enable wrapping on Development Plan controls
- dashboard-shell command groups remain responsive
- exact final PR head passes all eight established GitHub Actions workflows
- PR merges to `main` and Issue #195 closes Completed

# Runtime Health and Connection Center

Tracking issue: #30

## Goal

Give the operator a fast, read-only view of the dependencies that GPTDeskTop needs for normal operation without changing monitor, recovery, Chrome lifecycle, or delivery behavior.

## Health Inputs

- Chrome/CDP reachability via the existing `ChromeDevToolsService.GetTabsAsync` read path.
- Open ChatGPT tab count, based on the tab URL host (`chatgpt.com` / legacy `chat.openai.com`).
- SQLite reachability via the existing `LocalDatabase.GetSavedMonitorsAsync` read path.
- Saved monitor count from SQLite.
- Running monitor count from the existing `ChatGptMonitorService.IsMonitorRunning` state.

The health probe performs no monitor start/stop, Chrome reload, tab creation, message delivery, or database write.

## Health Levels

- `Healthy`: Chrome/CDP and SQLite are reachable. An empty workspace is healthy when no monitors are saved.
- `Degraded`: exactly one dependency is unavailable, or saved monitors exist while no ChatGPT conversation tab is open.
- `Unavailable`: both Chrome/CDP and SQLite probes fail.

## UX

- Compact top-docked panel, collapsed by default.
- `Details` / `Collapse` expansion with state persisted as `Ui.RuntimeHealth.Expanded`.
- Semantic status badge and per-dependency metric cards.
- Manual `Refresh` plus `F5` while the panel has focus.
- Five-second bounded probe timeout and duplicate-refresh guard.
- Accessibility names/descriptions for the panel, status, metrics, and actions.
- Running monitor count updates from the existing `RunningStateChanged` event without owning or restarting workers.

## Failure Handling

Probe failures are converted into health state instead of escaping into the UI thread. Non-timeout exceptions are written through `ExceptionLogService`. A failed probe does not block application shutdown or mutate runtime state.

## Validation

`RuntimeHealthPresentationTests` covers health-level rules, count clamping, and strict ChatGPT URL host detection.

`RuntimeHealthUiRegressionTests` locks the compact/DPI/accessibility contract, read-only probe behavior, timeout/duplicate protection, running-state subscription cleanup, and persisted expansion wiring.

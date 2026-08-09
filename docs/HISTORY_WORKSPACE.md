# Stored History Explorer

## Purpose

The Stored History Explorer is a post-1.8 operator workspace for investigating persisted GPTDeskTop activity without changing monitor, recovery, rotation, development-task, or shutdown behavior.

## UI behavior

- The explorer is docked at the bottom of the main window and is collapsed by default.
- Expand/collapse state is persisted in SQLite under `Ui.HistoryWorkspace.Expanded`.
- The explorer loads the latest 500 `MessageLogs` records and filters them in memory so searching does not mutate the database.
- Search matches chat title, flow, prompt, response, status, monitor ID, tab ID, and the displayed timestamp.
- Flow values are populated from persisted history.
- Status categories are normalized to `Issues`, `Success`, `Deferred`, and `Other` for fast operational triage.
- The result summary shows visible versus loaded rows and distinguishes an empty database from a no-match filter result.

## Operator actions

- **Clear Filters** resets Search, Flow, and Status.
- **Refresh** reloads the latest 500 persisted entries.
- **Copy Selected** copies a readable diagnostic block for the selected row.
- **Export Visible CSV** exports only the current filtered rows. Quotes, commas, carriage returns, and line feeds are escaped using standard CSV quoting rules; the file is written as UTF-8 with BOM for Excel/Windows compatibility.

## Keyboard

- `Ctrl+F` expands the explorer if necessary and focuses Search.
- `F5` refreshes history while focus is within the explorer.
- `Ctrl+C` copies the selected history entry only when the history grid owns focus.

## Safety contracts

The explorer is read-only except for writing a user-selected CSV file and the persisted expand/collapse preference. It does not delete history, start/stop monitors, change saved monitor settings, alter Chrome/CDP state, or participate in recovery/delivery decisions.

Regression coverage is in `HistoryWorkspaceLogicTests.cs` and `HistoryWorkspaceUiRegressionTests.cs`.

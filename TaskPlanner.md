# GPTDeskTop Project Implementation Plan

## Current Objective
Operate GPTDeskTop as a persistent multi-tab ChatGPT monitor manager. The application enumerates open debuggable Chrome tabs, allows any number of tabs to be added as saved monitors, gives every monitor its own automatic reply and enabled state, and runs each monitor independently and concurrently.

## Architecture
- **Open Tabs:** Chrome CDP discovery exposes Tab ID, Title and URL.
- **Saved Monitors:** SQLite `SavedMonitors` stores selected tabs and their individual auto-reply configuration.
- **Concurrent Runtime:** `ChatGptMonitorService` owns one cancellable worker per saved monitor ID.
- **Persistence:** `SavedMonitors`, `AppSettings`, and monitor-aware `MessageLogs` are stored in local `appdata.db`.
- **History Ownership:** every new log stores `MonitorId`, `TabId`, and `TabTitle` so concurrent conversations remain distinguishable.
- **CRUD:** add/save/delete monitor definitions; delete individual history rows or clear history.
- **Recovery:** saved monitor URLs are used to resolve a Chrome tab again if Chrome assigns a different runtime Tab ID after restart.

## Task Tracking Table

| Task ID | Task Name & Description | Suggested Role/Assignee | Priority | Status | Module / File Affected |
|---|---|---|---|---|---|
| DB-001 | Auto-create embedded SQLite DB | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-002 | Add persistent `SavedMonitors` table | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-003 | Upgrade existing `MessageLogs` with MonitorId/TabId/TabTitle without deleting old data | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-004 | Add monitor Save/Get/Delete CRUD | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-005 | Add application setting Get/Set persistence | Backend Engineer | Medium | Done | `Data/LocalDatabase.cs` |
| DB-006 | Add Delete Selected Log and Clear History persistence actions | Backend / UI | Medium | Done | `Data/LocalDatabase.cs`, `UI/MainForm.cs` |
| CHR-001 | Launch dedicated monitor Chrome profile with CDP | Browser Integration | High | Done | `Services/ChromeDevToolsService.cs` |
| CHR-002 | Enumerate open Chrome tabs with ID/Title/URL | Browser Integration | High | Done | `Services/ChromeDevToolsService.cs` |
| CHR-003 | Read ChatGPT assistant state and send prompt through DOM/CDP | Browser Integration | High | Done | `Services/ChromeDevToolsService.cs` |
| MON-001 | Replace single worker with independent worker dictionary keyed by Monitor ID | Backend Engineer | High | Done | `Services/ChatGptMonitorService.cs` |
| MON-002 | Start/stop one monitor without affecting other monitors | Backend Engineer | High | Done | `Services/ChatGptMonitorService.cs` |
| MON-003 | Start all enabled / stop all monitor workers | Backend / UI | High | Done | `Services/ChatGptMonitorService.cs`, `UI/MainForm.cs` |
| MON-004 | Maintain independent response de-duplication and stability state per tab | Backend Engineer | High | Done | `Services/ChatGptMonitorService.cs` |
| MON-005 | Resolve stale Chrome Tab ID by saved conversation URL | Backend / Browser Integration | High | Done | `UI/MainForm.cs` |
| UI-001 | Open Chrome Tabs grid | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-002 | Saved Monitors grid with Enabled/Status/AutoReply/Tab/URL | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-003 | Add Selected Tab action | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-004 | Save Monitor action for auto-reply/enabled/current tab metadata | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-005 | Delete Monitor action without closing Chrome tab | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-006 | Start Selected / Stop Selected controls | UI / Backend | High | Done | `UI/MainForm.cs` |
| UI-007 | Start All Enabled / Stop All controls | UI / Backend | High | Done | `UI/MainForm.cs` |
| UI-008 | Monitor-aware stored history grid | UI Developer | Medium | Done | `UI/MainForm.cs` |
| QA-001 | Build with .NET 8 SDK on Windows | QA Engineer | High | Not Started | Whole solution |
| QA-002 | Run 2+ ChatGPT tabs simultaneously and verify replies never cross tabs | QA Engineer | High | Not Started | Runtime |
| QA-003 | Restart app/Chrome and verify saved monitors reload and URL fallback reconnects | QA Engineer | High | Not Started | Runtime / DB |
| QA-004 | Verify Delete Monitor does not delete history and does not close Chrome tab | QA Engineer | Medium | Not Started | Runtime |
| QA-005 | Validate current ChatGPT DOM selectors against live site | QA / Browser Integration | High | Not Started | `ChromeDevToolsService.cs` |

## Acceptance Criteria
- Any number of open Chrome tabs can be added as saved monitors.
- Every saved monitor has its own persisted automatic reply and enabled state.
- Multiple monitors can run simultaneously and independently.
- Starting/stopping one monitor does not stop other monitor workers.
- Start All starts every enabled monitor whose matching Chrome tab is currently open.
- Saved monitor configuration survives application restarts.
- If a runtime Tab ID changes, exact saved URL can reconnect the monitor to the reopened conversation.
- Logs identify the originating Monitor ID and tab.
- Monitor definitions support Add, Save and Delete.
- History supports refresh, delete selected row and clear all.
- Existing database data is upgraded in place; no destructive reset is required.

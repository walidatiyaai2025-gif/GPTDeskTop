# GPTDeskTop Project Implementation Plan

## Current Objective
Operate GPTDeskTop as a persistent multi-tab ChatGPT monitor manager. The application enumerates open debuggable Chrome tabs, allows any number of tabs to be added as saved monitors, gives every monitor its own automatic reply and enabled state, runs each monitor independently and concurrently, notifies the operator from the Windows taskbar whenever ChatGPT produces a new response, automatically recovers a monitored tab when ChatGPT returns an error response, and supports a persisted configurable delay before each automatic reply is sent.

## Architecture
- **Open Tabs:** Chrome CDP discovery exposes Tab ID, Title and URL.
- **Saved Monitors:** SQLite `SavedMonitors` stores selected tabs and their individual auto-reply configuration.
- **Concurrent Runtime:** `ChatGptMonitorService` owns one cancellable worker per saved monitor ID.
- **Persistence:** `SavedMonitors`, `AppSettings`, and monitor-aware `MessageLogs` are stored in local `appdata.db`.
- **History Ownership:** every new log stores `MonitorId`, `TabId`, and `TabTitle` so concurrent conversations remain distinguishable.
- **CRUD:** add/save/delete monitor definitions; delete individual history rows or clear history.
- **Recovery:** saved monitor URLs are used to resolve a Chrome tab again if Chrome assigns a different runtime Tab ID after restart.
- **Notifications:** `TrayNotificationService` shows a Windows taskbar/tray balloon for every completed response and persists the chosen display duration in `AppSettings`.
- **ChatGPT Error Recovery:** page/response errors are detected, stored before any recovery action, surfaced as an error balloon, and the affected tab alone is refreshed through CDP `Page.reload`.
- **Reply Delay:** `ReplyDelaySeconds` is stored in `AppSettings`, loaded immediately before every send, and can be changed at runtime from the Settings dialog without restarting monitors.

## Task Tracking Table

| Task ID | Task Name & Description | Suggested Role/Assignee | Priority | Status | Module / File Affected |
|---|---|---|---|---|---|
| DB-001 | Auto-create embedded SQLite DB | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-002 | Add persistent `SavedMonitors` table | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-003 | Upgrade existing `MessageLogs` with MonitorId/TabId/TabTitle without deleting old data | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-004 | Add monitor Save/Get/Delete CRUD | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-005 | Add application setting Get/Set persistence | Backend Engineer | Medium | Done | `Data/LocalDatabase.cs` |
| DB-006 | Add Delete Selected Log and Clear History persistence actions | Backend / UI | Medium | Done | `Data/LocalDatabase.cs`, `UI/MainForm.cs` |
| DB-007 | Persist notification balloon duration with default migration value | Backend / DBA | Medium | Done | `Data/LocalDatabase.cs`, `AppSettings` |
| DB-008 | Persist `ReplyDelaySeconds` with default value 3 seconds and typed integer settings reader | Backend / DBA | High | Done | `Data/LocalDatabase.cs`, `AppSettings` |
| CHR-001 | Launch dedicated monitor Chrome profile with CDP | Browser Integration | High | Done | `Services/ChromeDevToolsService.cs` |
| CHR-002 | Enumerate open Chrome tabs with ID/Title/URL | Browser Integration | High | Done | `Services/ChromeDevToolsService.cs` |
| CHR-003 | Read ChatGPT assistant state and send prompt through DOM/CDP | Browser Integration | High | Done | `Services/ChromeDevToolsService.cs` |
| CHR-004 | Detect visible ChatGPT page error messages and return error text with page state | Browser Integration | High | Done | `Services/ChromeDevToolsService.cs`, `Models/Models.cs` |
| CHR-005 | Reload one selected Chrome tab through CDP `Page.reload` | Browser Integration | High | Done | `Services/ChromeDevToolsService.cs` |
| MON-001 | Replace single worker with independent worker dictionary keyed by Monitor ID | Backend Engineer | High | Done | `Services/ChatGptMonitorService.cs` |
| MON-002 | Start/stop one monitor without affecting other monitors | Backend Engineer | High | Done | `Services/ChatGptMonitorService.cs` |
| MON-003 | Start all enabled / stop all monitor workers | Backend / UI | High | Done | `Services/ChatGptMonitorService.cs`, `UI/MainForm.cs` |
| MON-004 | Maintain independent response de-duplication and stability state per tab | Backend Engineer | High | Done | `Services/ChatGptMonitorService.cs` |
| MON-005 | Resolve stale Chrome Tab ID by saved conversation URL | Backend / Browser Integration | High | Done | `UI/MainForm.cs` |
| MON-006 | Save every received ChatGPT response before subsequent send/recovery action | Backend / DBA | High | Done | `Services/ChatGptMonitorService.cs` |
| MON-007 | Classify ChatGPT error responses, save them with `Error` status, refresh only affected tab, and log refresh result | Backend / Browser Integration | High | Done | `Services/ChatGptMonitorService.cs`, `ChromeDevToolsService.cs` |
| MON-008 | Wait configurable 0-300 seconds before auto reply, support cancellation, and recheck page state before sending | Backend Engineer | High | Done | `Services/ChatGptMonitorService.cs`, `Data/LocalDatabase.cs` |
| NOT-001 | Show tray/taskbar balloon for every completed ChatGPT response | UI / Backend | High | Done | `Services/TrayNotificationService.cs` |
| NOT-002 | Show error-style balloon for error responses | UI Developer | High | Done | `Services/TrayNotificationService.cs` |
| NOT-003 | Provide tray menu choices for notification duration and save immediately | UI / Backend | Medium | Done | `Services/TrayNotificationService.cs`, `Data/LocalDatabase.cs` |
| UI-001 | Open Chrome Tabs grid | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-002 | Saved Monitors grid with Enabled/Status/AutoReply/Tab/URL | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-003 | Add Selected Tab action | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-004 | Save Monitor action for auto-reply/enabled/current tab metadata | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-005 | Delete Monitor action without closing Chrome tab | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-006 | Start Selected / Stop Selected controls | UI / Backend | High | Done | `UI/MainForm.cs` |
| UI-007 | Start All Enabled / Stop All controls | UI / Backend | High | Done | `UI/MainForm.cs` |
| UI-008 | Monitor-aware stored history grid | UI Developer | Medium | Done | `UI/MainForm.cs` |
| UI-009 | Add Settings dialog for reply delay and balloon duration, persisted immediately | UI / Backend | High | Done | `UI/SettingsForm.cs`, `Services/TrayNotificationService.cs` |
| QA-001 | Build with .NET 8 SDK on Windows | QA Engineer | High | Not Started | Whole solution |
| QA-002 | Run 2+ ChatGPT tabs simultaneously and verify replies never cross tabs | QA Engineer | High | Not Started | Runtime |
| QA-003 | Restart app/Chrome and verify saved monitors reload and URL fallback reconnects | QA Engineer | High | Not Started | Runtime / DB |
| QA-004 | Verify Delete Monitor does not delete history and does not close Chrome tab | QA Engineer | Medium | Not Started | Runtime |
| QA-005 | Validate current ChatGPT DOM selectors/error selectors against live site | QA / Browser Integration | High | Not Started | `ChromeDevToolsService.cs` |
| QA-006 | Verify normal replies produce info balloon and error replies produce error balloon | QA Engineer | High | Not Started | Runtime / Tray |
| QA-007 | Verify selected balloon duration persists across app restarts | QA Engineer | Medium | Not Started | Runtime / DB |
| QA-008 | Force a ChatGPT error and verify response is stored before Page.reload and only that tab refreshes | QA Engineer | High | Not Started | Runtime / DB / Chrome |
| QA-009 | Set reply delay to 0, 3, 10, and 300 seconds and verify send timing plus Stop cancellation | QA Engineer | High | Not Started | Runtime / Settings |
| QA-010 | Change reply delay while monitors are running and verify next send uses the new value without restart | QA Engineer | High | Not Started | Runtime / DB |

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
- Every completed ChatGPT response is saved and raises a Windows taskbar/tray balloon containing the monitor identity and a shortened response preview.
- Balloon duration is selectable and persisted in SQLite.
- A detected ChatGPT error response is saved as `Inbound / Error` before recovery starts.
- Error recovery refreshes only the affected monitored tab through CDP and stores the refresh result in history.
- Reply delay is configurable from 0 to 300 seconds, persisted as `ReplyDelaySeconds`, and applied before every automatic send.
- Stopping a monitor during the delay cancels the pending send.
- Before sending after a delay, GPTDeskTop rechecks the tab; if the response changed or generation restarted, the pending auto reply is cancelled and logged as `SendDelayCancelled`.

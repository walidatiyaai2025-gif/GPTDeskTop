# GPTDeskTop Project Implementation Plan

## Current Objective
Maintain GPTDeskTop as a persistent .NET 8 multi-tab ChatGPT monitor with independent per-tab timing, SQLite persistence, tray notifications, automatic timeout recovery into a new ChatGPT conversation, Chrome lifecycle control, Fluent/WinUI-inspired WinForms UX, and a Visual Studio-buildable standalone Setup EXE.

## Architecture
- **Application:** .NET 8 WinForms, version `1.5.0`.
- **Browser:** dedicated Chrome profile controlled through Chrome DevTools Protocol.
- **Saved Monitors:** independent Auto Reply, Reply Delay, Monitor Timer and Enabled state per tab.
- **Runtime:** one cancellable worker per Monitor ID.
- **Persistence:** SQLite `appdata.db` with in-place schema upgrades and application defaults.
- **Notifications:** Windows tray balloons with configurable duration and application sound.
- **Normal Error Recovery:** save error, refresh only affected tab.
- **Delivery Timeout Recovery:** save timeout, create a new ChatGPT tab, send recovery message, move the existing Monitor ID to the new tab, continue monitoring, close old timed-out tab.
- **Chrome Lifecycle:** hide/show monitor Chrome while workers run; close all monitor Chrome tabs when GPTDeskTop exits.
- **UX:** Fluent/WinUI-inspired WinForms styling plus right-click context menus on all data grids.
- **Release pipeline:** application -> publish -> standalone setup, all SDK-style Visual Studio projects.

## Task Tracking Table

| Task ID | Task Name & Description | Suggested Role/Assignee | Priority | Status | Module / File Affected |
|---|---|---|---|---|---|
| DB-001 | Auto-create/upgrade SQLite database | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-002 | Persist saved monitor CRUD and monitor-aware logs | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-003 | Persist per-monitor Delay/Timer | Backend / DBA | High | Done | `SavedMonitors`, `Models.cs` |
| DB-004 | Persist DefaultAutoReply, DefaultMonitorDelaySeconds and DefaultMonitorTimerSeconds | Backend / DBA | High | Done | `AppSettings` |
| DB-005 | Persist TimeoutRecoveryMessage and notification sound settings | Backend / DBA | High | Done | `AppSettings` |
| MON-001 | Independent worker per Monitor ID | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| MON-002 | Per-monitor polling Timer and reply Delay | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| MON-003 | Detect `Message delivery timed out` as a dedicated recovery condition | Browser Integration | High | Done | `ChromeDevToolsService.cs` |
| MON-004 | Save timeout before recovery action | Backend / DBA | High | Done | `ChatGptMonitorService.cs` |
| MON-005 | Create new ChatGPT tab and send configured recovery message | Browser Integration | High | Done | `ChromeDevToolsService.cs`, `ChatGptMonitorService.cs` |
| MON-006 | Move existing Saved Monitor to recovery tab and continue same worker/Monitor ID | Backend Engineer | High | Done | `ChatGptMonitorService.cs`, SQLite |
| MON-007 | Close old timed-out tab after successful recovery | Browser Integration | Medium | Done | `ChromeDevToolsService.cs` |
| CHR-001 | Launch/enumerate/hide/show monitor Chrome | Browser Integration | High | Done | `ChromeDevToolsService.cs` |
| CHR-002 | Close selected Chrome tab from grid context menu | Browser Integration / UI | Medium | Done | `MainForm.cs`, `ChromeDevToolsService.cs` |
| CHR-003 | Close all monitor Chrome tabs when application exits | Browser Integration | High | Done | `MainForm.cs`, `ChromeDevToolsService.cs` |
| UI-001 | Per-tab settings dialog during Add Monitor | UI Developer | High | Done | `MonitorSettingsForm.cs` |
| UI-002 | Fluent/WinUI-inspired visual system for WinForms | UI Developer | High | Done | `FluentTheme.cs`, forms |
| UI-003 | Modernize buttons, surfaces, typography and grids | UI Developer | High | Done | `MainForm.cs`, `SettingsForm.cs`, `MonitorSettingsForm.cs` |
| UI-004 | Add context menu to Open Tabs grid | UI Developer | Medium | Done | `MainForm.cs` |
| UI-005 | Add context menu to Saved Monitors grid | UI Developer | Medium | Done | `MainForm.cs` |
| UI-006 | Add context menu to History grid | UI Developer | Medium | Done | `MainForm.cs` |
| UI-007 | Add main Settings button and persisted default monitor settings | UI / Backend | High | Done | `MainForm.cs`, `SettingsForm.cs` |
| NOT-001 | Configurable balloon duration | UI / Backend | Medium | Done | `TrayNotificationService.cs` |
| NOT-002 | Configurable balloon application sound enable/type | UI / Backend | High | Done | `TrayNotificationService.cs`, `SettingsForm.cs` |
| REL-001 | Bump application and setup to `1.5.0` | Release Engineer | Medium | Done | csproj/setup files |
| REL-002 | Keep three SDK-style projects in Visual Studio solution | Release / DevOps | High | Done | `GPTDeskTop.sln` |
| REL-003 | Generate standalone `Output/Setup/GPTDeskTop-Setup.exe` | Release / DevOps | High | Done | Publish/Setup projects |
| QA-001 | Build `Release | x64` in Visual Studio | QA Engineer | High | Not Started | Whole solution |
| QA-002 | Reproduce message delivery timeout and verify new-chat recovery | QA Engineer | High | Not Started | Runtime / Chrome |
| QA-003 | Verify recovery keeps same Monitor ID and per-tab Delay/Timer | QA Engineer | High | Not Started | Runtime / SQLite |
| QA-004 | Close GPTDeskTop and verify all dedicated Chrome tabs close | QA Engineer | High | Not Started | Runtime / Chrome |
| QA-005 | Verify all three grid context menus target correct selected row | QA Engineer | Medium | Not Started | UI |
| QA-006 | Verify notification sound enable/type and duration persist after restart | QA Engineer | Medium | Not Started | Tray / SQLite |
| QA-007 | Verify default monitor settings apply to newly added tabs | QA Engineer | Medium | Not Started | UI / SQLite |

## Acceptance Criteria
- A `Message delivery timed out` response is stored before recovery.
- Timeout recovery opens a fresh ChatGPT chat and sends the configured recovery message (default `كمل`).
- The existing monitor is rebound to the new tab under the same Monitor ID and continues running.
- The old timed-out tab is closed after successful recovery.
- All dedicated Monitor Chrome tabs close when GPTDeskTop exits.
- New monitors inherit persisted default Auto Reply, Delay and Timer values.
- Every monitor can still override its own Delay and Timer.
- Balloon duration and application sound settings are persisted in SQLite.
- Open Tabs, Saved Monitors and History grids expose useful right-click actions.
- Main WinForms interface uses a consistent Fluent/WinUI-inspired visual system.
- `GPTDeskTop.sln` remains composed of exactly three supported SDK-style projects.

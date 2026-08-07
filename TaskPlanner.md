# GPTDeskTop Project Implementation Plan

## Current Objective
Maintain GPTDeskTop as a persistent .NET 8 multi-tab ChatGPT monitor with independent per-tab timing, SQLite persistence, tray notifications, automatic timeout recovery, Chrome lifecycle control, Fluent/WinUI-inspired WinForms UX, persistent exception diagnostics, and a Visual Studio-buildable standalone Setup EXE.

## Architecture
- **Application:** .NET 8 WinForms, version `1.6.0`.
- **Browser:** dedicated Chrome profile controlled through Chrome DevTools Protocol.
- **Saved Monitors:** independent Auto Reply, Reply Delay, Monitor Timer and Enabled state per tab.
- **Runtime:** one cancellable worker per Monitor ID.
- **Persistence:** SQLite `appdata.db` with monitor configuration, defaults, history and exception records.
- **Exception Diagnostics:** unhandled UI/domain/task exceptions and monitor exceptions are written to `MessageLogs` with status `Exception` and to `logs/exceptions-YYYYMMDD.log` with full stack trace.
- **No-response Watchdog:** global `NoResponseRefreshSeconds`, default `180` seconds; only the affected tab is refreshed when no new assistant response arrives within the configured period.
- **Notifications:** Windows tray balloons with configurable duration and sound.
- **Normal Error Recovery:** save error, refresh only affected tab.
- **Delivery Timeout Recovery:** save timeout, create a new ChatGPT tab, send recovery message, move the existing Monitor ID to the new tab, continue monitoring, close old timed-out tab.
- **Chrome Lifecycle:** CDP-first minimize/hide/show behavior while workers continue; close monitor tabs on exit.
- **UX:** Fluent/WinUI-inspired WinForms styling, right-click context menus and green/red runtime indicators.
- **Release pipeline:** application -> publish -> standalone setup, all SDK-style Visual Studio projects.

## Task Tracking Table

| Task ID | Task Name & Description | Suggested Role/Assignee | Priority | Status | Module / File Affected |
|---|---|---|---|---|---|
| DB-001 | Auto-create/upgrade SQLite database | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-002 | Persist monitor CRUD, defaults and history | Backend / DBA | High | Done | SQLite / `LocalDatabase.cs` |
| DB-003 | Persist no-response timeout default `180` seconds | Backend / DBA | High | Done | `AppSettings`, `Program.cs`, `SettingsForm.cs` |
| DIA-001 | Add persistent file exception logger with full stack trace | Backend Engineer | High | Done | `ExceptionLogService.cs` |
| DIA-002 | Hook WinForms UI, AppDomain and unobserved Task exceptions | Backend Engineer | High | Done | `Program.cs` |
| DIA-003 | Save monitor/runtime exceptions into in-app Stored History | Backend Engineer | High | Done | `ChatGptMonitorService.cs`, `MessageLogs` |
| MON-001 | Independent worker per Monitor ID | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| MON-002 | Per-monitor polling Timer and reply Delay | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| MON-003 | Refresh only affected tab after configurable no-response period | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| MON-004 | Delivery-timeout new-chat recovery under same Monitor ID | Browser / Backend | High | Done | Chrome/Monitor services |
| CHR-001 | Improve Hide/Show using CDP window state before native fallback | Browser Integration | High | Done | `ChromeDevToolsService.cs` |
| CHR-002 | Close monitor tabs when application exits | Browser Integration | High | Done | Main/Chrome services |
| UI-001 | Fluent/WinUI-inspired WinForms visual system | UI Developer | High | Done | `FluentTheme.cs`, forms |
| UI-002 | Right-click menus for Open Tabs / Saved Monitors / History | UI Developer | Medium | Done | `MainForm.cs` |
| UI-003 | Green/red runtime lamp in monitor Status column | UI Developer | Medium | Done | `Models.cs`, Saved Monitors grid |
| UI-004 | Add no-response timeout field to Settings | UI / Backend | High | Done | `SettingsForm.cs` |
| NOT-001 | Configurable balloon duration and sound | UI / Backend | High | Done | Notification/Settings services |
| REL-001 | Bump application, publish and setup metadata to `1.6.0` | Release Engineer | Medium | Done | csproj / setup files |
| QA-001 | Build `Release | x64` in Visual Studio | QA Engineer | High | Not Started | Whole solution |
| QA-002 | Capture one reported `InvalidOperationException` and verify full stack trace in Stored History/file log | QA Engineer | High | Not Started | Diagnostics |
| QA-003 | Run monitor hidden for 10+ minutes and verify CDP polling continues | QA Engineer | High | Not Started | Chrome / Runtime |
| QA-004 | Set no-response timeout to 30 seconds and verify exactly one affected tab refreshes | QA Engineer | High | Not Started | Runtime |
| QA-005 | Verify green lamp while monitor runs and red lamp when stopped | QA Engineer | Medium | Not Started | UI |
| QA-006 | Verify timeout recovery, tray sounds, database persistence and exit tab cleanup | QA Engineer | High | Not Started | Runtime |

## Acceptance Criteria
- Every relevant exception is visible in Stored History with status `Exception` and written with full stack trace under `logs`.
- `NoResponseRefreshSeconds` defaults to `180` and is editable in seconds from Settings.
- A monitor with no new assistant response for the configured period refreshes only its own tab and continues monitoring.
- Hide Chrome does not stop background polling/auto-response.
- Saved Monitors display a green running indicator and red stopped indicator.
- Existing per-tab Delay/Timer and delivery-timeout recovery continue to operate.
- `GPTDeskTop.sln` remains composed of exactly three supported SDK-style projects.

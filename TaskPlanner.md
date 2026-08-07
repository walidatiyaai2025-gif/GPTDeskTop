# GPTDeskTop Project Implementation Plan

## Current Objective
Maintain GPTDeskTop as a persistent .NET 8 multi-tab ChatGPT monitor with independent per-tab timing, SQLite persistence, tray notifications, automatic timeout/crash recovery, Chrome lifecycle control, Fluent/WinUI-inspired WinForms UX, persistent exception diagnostics, and a Visual Studio-buildable standalone Setup EXE.

## Architecture
- **Application:** .NET 8 WinForms, version `1.7.0`.
- **Browser:** dedicated Chrome profile controlled through Chrome DevTools Protocol.
- **Saved Monitors:** independent Auto Reply, Reply Delay, Monitor Timer and Enabled state per tab.
- **Runtime:** one cancellable worker per Monitor ID.
- **Persistence:** SQLite `appdata.db` with monitor configuration, defaults, history, crash counters and exception records.
- **Transient CDP Recovery:** `Promise was collected` from `Runtime.evaluate` is retried internally up to three times before being treated as a real monitor failure.
- **Exception Diagnostics:** unhandled UI/domain/task exceptions and monitor exceptions are written to `MessageLogs` and `logs/exceptions-YYYYMMDD.log`.
- **No-response Watchdog:** global `NoResponseRefreshSeconds`, default `180` seconds; only the affected tab is refreshed when no new assistant response arrives within the configured period.
- **Crash Detection:** `LastShutdownClean` is set to `0` for the running process and changed to `1` only after a graceful MainForm close. A subsequent startup seeing `0` increments `CrashCount` and schedules full session recovery.
- **Crash Session Recovery:** close leftover monitor tabs, reopen all saved monitor URLs, send the configured recovery message (default `كمل`), update saved Tab IDs, then restart enabled monitor workers.
- **Fatal Auto Restart:** a fatal exception escaping the app attempts one automatic process restart with a 30-second loop guard.
- **Notifications:** Windows tray balloons with configurable duration and sound.
- **Delivery Timeout Recovery:** save timeout, create a new ChatGPT tab, send recovery message, move the existing Monitor ID to the new tab, continue monitoring, close old timed-out tab.
- **Chrome Lifecycle:** CDP-first minimize/hide/show behavior while workers continue; close monitor tabs on exit.
- **UX:** Fluent/WinUI-inspired WinForms styling, right-click context menus, green/red runtime lamp, Crash Count card and Monitor Count card.
- **Release pipeline:** application -> publish -> standalone setup, all SDK-style Visual Studio projects.

## Task Tracking Table

| Task ID | Task Name & Description | Suggested Role/Assignee | Priority | Status | Module / File Affected |
|---|---|---|---|---|---|
| DB-001 | Auto-create/upgrade SQLite database | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-002 | Persist monitor CRUD, defaults and history | Backend / DBA | High | Done | SQLite / `LocalDatabase.cs` |
| DB-003 | Persist no-response timeout default `180` seconds | Backend / DBA | High | Done | `AppSettings`, `Program.cs`, `SettingsForm.cs` |
| DB-004 | Persist crash markers and `CrashCount` | Backend / DBA | High | Done | `Program.cs`, `AppSettings` |
| DIA-001 | Add persistent file exception logger with full stack trace | Backend Engineer | High | Done | `ExceptionLogService.cs` |
| DIA-002 | Hook WinForms UI, AppDomain and unobserved Task exceptions | Backend Engineer | High | Done | `Program.cs` |
| DIA-003 | Save monitor/runtime exceptions into in-app Stored History | Backend Engineer | High | Done | `ChatGptMonitorService.cs`, `MessageLogs` |
| MON-001 | Independent worker per Monitor ID | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| MON-002 | Per-monitor polling Timer and reply Delay | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| MON-003 | Refresh only affected tab after configurable no-response period | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| MON-004 | Delivery-timeout new-chat recovery under same Monitor ID | Browser / Backend | High | Done | Chrome/Monitor services |
| MON-005 | Retry transient CDP `Promise was collected` failures instead of logging each as crash | Browser Integration | High | Done | `ChromeDevToolsService.cs` |
| CRASH-001 | Detect unclean shutdown and increment persistent crash count | Backend Engineer | High | Done | `Program.cs` |
| CRASH-002 | Auto restart once after fatal crash with restart-loop guard | Backend Engineer | High | Done | `Program.cs` |
| CRASH-003 | Reopen all saved monitor tabs and send recovery message after crash | Browser / Backend | High | Done | `CrashRecoveryService.cs` |
| CRASH-004 | Rebind saved monitors to recreated Chrome Tab IDs and restart enabled workers | Backend Engineer | High | Done | `CrashRecoveryService.cs` |
| CHR-001 | Improve Hide/Show using CDP window state plus native hide fallback | Browser Integration | High | Done | `ChromeDevToolsService.cs` |
| CHR-002 | Close monitor tabs when application exits | Browser Integration | High | Done | Main/Chrome services |
| UI-001 | Fluent/WinUI-inspired WinForms visual system | UI Developer | High | Done | `FluentTheme.cs`, forms |
| UI-002 | Right-click menus for Open Tabs / Saved Monitors / History | UI Developer | Medium | Done | `MainForm.cs` |
| UI-003 | Green/red runtime lamp in monitor Status column | UI Developer | Medium | Done | `HomeMetricsService.cs` |
| UI-004 | Add Crash Count and Monitor Count home cards | UI Developer | Medium | Done | `HomeMetricsService.cs` |
| UI-005 | Add no-response timeout field to Settings | UI / Backend | High | Done | `SettingsForm.cs` |
| NOT-001 | Configurable balloon duration and sound | UI / Backend | High | Done | Notification/Settings services |
| REL-001 | Bump application, publish and setup metadata to `1.7.0` | Release Engineer | Medium | Done | csproj / setup files |
| QA-001 | Build `Release | x64` in Visual Studio | QA Engineer | High | Not Started | Whole solution |
| QA-002 | Reproduce `Promise was collected` and verify transient retry prevents repeated exception log entries | QA Engineer | High | Not Started | Chrome integration |
| QA-003 | Force-kill GPTDeskTop, relaunch and verify Crash Count increments and saved tabs recover | QA Engineer | High | Not Started | Crash recovery |
| QA-004 | Verify every recovered tab receives `كمل`/configured recovery message and enabled monitors restart | QA Engineer | High | Not Started | Crash recovery |
| QA-005 | Run monitor hidden for 10+ minutes and verify CDP polling continues | QA Engineer | High | Not Started | Chrome / Runtime |
| QA-006 | Set no-response timeout to 30 seconds and verify exactly one affected tab refreshes | QA Engineer | High | Not Started | Runtime |
| QA-007 | Verify green lamp while monitor runs, red lamp when stopped, and home cards update correctly | QA Engineer | Medium | Not Started | UI |

## Acceptance Criteria
- Transient `Promise was collected` errors are retried automatically and do not flood the exception history.
- Every real exception remains visible in Stored History and the exception log file.
- `NoResponseRefreshSeconds` defaults to `180` and remains editable in seconds.
- A monitor with no new assistant response for the configured period refreshes only its own tab and continues monitoring.
- A true unclean shutdown increments the persistent Crash Count on next startup.
- After an unclean shutdown, GPTDeskTop closes leftover monitor tabs, recreates saved tabs, sends the recovery message and restarts enabled monitors.
- Fatal crashes attempt one automatic restart without entering a restart loop.
- Hide Chrome does not stop background polling/auto-response.
- Saved Monitors display a green running lamp and red stopped lamp.
- Home displays persistent Crash Count and live/total Monitor Count cards.
- `GPTDeskTop.sln` remains composed of exactly three supported SDK-style projects.

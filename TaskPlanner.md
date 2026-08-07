# GPTDeskTop Project Implementation Plan

## Current Objective
Maintain GPTDeskTop as a persistent .NET 8 multi-tab ChatGPT monitor with independent monitor configuration, SQLite persistence, tray notifications, Chrome error recovery, Chrome hide/show controls, visible release identity, and a Visual Studio-buildable Windows Setup package.

## Architecture
- **Application:** .NET 8 WinForms, current version `1.3.0`.
- **Browser:** dedicated Chrome profile controlled through Chrome DevTools Protocol.
- **Saved Monitors:** every tab has independent Auto Reply, Reply Delay, Monitor Timer and Enabled state.
- **Runtime:** one cancellable worker per Monitor ID.
- **Persistence:** SQLite `appdata.db` with in-place schema upgrades.
- **Notifications:** Windows tray balloons for completed replies and errors.
- **Recovery:** save ChatGPT error response first, then refresh only the affected tab.
- **Installer:** WiX Toolset 5 projects inside `GPTDeskTop.sln`; MSI plus final `GPTDeskTop-Setup.exe` bootstrapper.

## Task Tracking Table

| Task ID | Task Name & Description | Suggested Role/Assignee | Priority | Status | Module / File Affected |
|---|---|---|---|---|---|
| DB-001 | Auto-create/upgrade SQLite database | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-002 | Persist saved monitor CRUD | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-003 | Persist MonitorId/TabId/TabTitle in history | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-004 | Add per-monitor `ReplyDelaySeconds` and `TimerSeconds` with non-destructive migration | Backend / DBA | High | Done | `Data/LocalDatabase.cs`, `Models/Models.cs` |
| CHR-001 | Launch/enumerate Chrome CDP tabs | Browser Integration | High | Done | `ChromeDevToolsService.cs` |
| CHR-002 | Read ChatGPT state and send replies | Browser Integration | High | Done | `ChromeDevToolsService.cs` |
| CHR-003 | Detect errors and refresh only affected tab | Browser Integration | High | Done | `ChromeDevToolsService.cs`, `ChatGptMonitorService.cs` |
| CHR-004 | Hide/show Monitor Chrome while workers continue | Browser Integration / UI | High | Done | `ChromeDevToolsService.cs`, `MainForm.cs` |
| MON-001 | Independent concurrent worker per Monitor ID | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| MON-002 | Per-monitor polling Timer (`1-60` seconds) | Backend Engineer | High | Done | `ChatGptMonitorService.cs`, `SavedMonitors` |
| MON-003 | Per-monitor reply Delay (`0-300` seconds) | Backend Engineer | High | Done | `ChatGptMonitorService.cs`, `SavedMonitors` |
| MON-004 | Cancel delayed send when monitor stops or page state changes | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| UI-001 | Open Chrome tabs grid and multi-select add | UI Developer | High | Done | `MainForm.cs` |
| UI-002 | Per-tab settings dialog during Add Selected Tab(s) | UI Developer | High | Done | `MonitorSettingsForm.cs`, `MainForm.cs` |
| UI-003 | Edit saved monitor Auto Reply/Delay/Timer/Enabled | UI Developer | High | Done | `MonitorSettingsForm.cs`, `MainForm.cs` |
| UI-004 | Display Delay and Timer columns in Saved Monitors grid | UI Developer | Medium | Done | `MainForm.cs` |
| UI-005 | Tray notifications and configurable balloon duration | UI / Backend | High | Done | `TrayNotificationService.cs` |
| UI-006 | Visible application version in title/footer | UI / Release | Medium | Done | `MainForm.cs`, `GPTDeskTop.csproj` |
| REL-001 | Set application version to `1.3.0` | Release Engineer | Medium | Done | `GPTDeskTop.csproj` |
| REL-002 | Add WiX MSI project to Visual Studio solution | Release / DevOps | High | Done | `GPTDeskTop.Setup/*`, `GPTDeskTop.sln` |
| REL-003 | Publish win-x64 self-contained payload during setup build | Release / DevOps | High | Done | `GPTDeskTop.Setup.wixproj` |
| REL-004 | Add WiX Bootstrapper that produces `GPTDeskTop-Setup.exe` | Release / DevOps | High | Done | `GPTDeskTop.Bootstrapper/*`, `GPTDeskTop.sln` |
| REL-005 | Ignore generated installer payload/bin/obj from Git | DevOps | Medium | Done | `.gitignore` |
| DOC-001 | Document Visual Studio Setup build and per-tab timing workflow | Tech Lead | Medium | Done | `README.md` |
| QA-001 | Build application with .NET 8 on Windows | QA Engineer | High | Not Started | Whole solution |
| QA-002 | Build `Release | x64` Setup from Visual Studio with HeatWave/WiX support | QA / Release | High | Not Started | Setup projects |
| QA-003 | Install generated Setup EXE on a clean Windows machine | QA Engineer | High | Not Started | Installer |
| QA-004 | Run 2+ tabs with different Delay and Timer values and verify isolation | QA Engineer | High | Not Started | Runtime |
| QA-005 | Verify old DB upgrades without losing monitors/history | QA / DBA | High | Not Started | SQLite migration |

## Acceptance Criteria
- Each selected Chrome tab opens its own settings dialog before becoming a saved monitor.
- Each monitor persists an independent Auto Reply, Delay (`0-300s`) and Timer (`1-60s`).
- Monitor Timer controls that monitor's own polling cadence only.
- Reply Delay controls that monitor's own wait before auto-send only.
- Existing SQLite databases upgrade in place without destructive reset.
- Saved Monitors grid shows Delay and Timer values.
- `GPTDeskTop.sln` includes the application, MSI setup project and Setup EXE bootstrapper project.
- Visual Studio `Release | x64` build can produce a self-contained installer after WiX/HeatWave support is installed.
- Final installer output is `GPTDeskTop-Setup.exe`.

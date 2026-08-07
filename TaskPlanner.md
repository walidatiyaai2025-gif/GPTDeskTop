# GPTDeskTop Project Implementation Plan

## Current Objective
Maintain GPTDeskTop as a persistent .NET 8 multi-tab ChatGPT monitor with independent per-tab timing, SQLite persistence, tray notifications, Chrome recovery/visibility controls, visible release identity, and a Visual Studio-buildable standalone Setup EXE without WiX project dependencies.

## Architecture
- **Application:** .NET 8 WinForms, version `1.4.0`.
- **Browser:** dedicated Chrome profile controlled through Chrome DevTools Protocol.
- **Saved Monitors:** every tab has independent Auto Reply, Reply Delay, Monitor Timer and Enabled state.
- **Runtime:** one cancellable worker per Monitor ID.
- **Persistence:** SQLite `appdata.db` with in-place schema upgrades.
- **Notifications:** Windows tray balloons for completed replies and errors.
- **Recovery:** save ChatGPT error response first, then refresh only the affected tab.
- **Release pipeline:** three SDK-style projects in one solution: application -> publish -> setup.
- **Installer:** self-contained single-file `GPTDeskTop-Setup.exe` generated under `Output/Setup`.

## Task Tracking Table

| Task ID | Task Name & Description | Suggested Role/Assignee | Priority | Status | Module / File Affected |
|---|---|---|---|---|---|
| DB-001 | Auto-create/upgrade SQLite database | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-002 | Persist saved monitor CRUD | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-003 | Persist MonitorId/TabId/TabTitle in history | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| DB-004 | Persist per-monitor `ReplyDelaySeconds` and `TimerSeconds` | Backend / DBA | High | Done | `Data/LocalDatabase.cs`, `Models/Models.cs` |
| MON-001 | Independent worker per Monitor ID | Backend Engineer | High | Done | `ChatGptMonitorService.cs` |
| MON-002 | Per-monitor polling Timer (`1-60` seconds) | Backend Engineer | High | Done | `ChatGptMonitorService.cs`, `SavedMonitors` |
| MON-003 | Per-monitor reply Delay (`0-300` seconds) | Backend Engineer | High | Done | `ChatGptMonitorService.cs`, `SavedMonitors` |
| UI-001 | Per-tab settings dialog during Add Selected Tab(s) | UI Developer | High | Done | `MonitorSettingsForm.cs`, `MainForm.cs` |
| UI-002 | Display Delay and Timer columns in Saved Monitors | UI Developer | Medium | Done | `MainForm.cs` |
| UI-003 | Tray notifications, visible version and Chrome hide/show | UI / Backend | High | Done | UI/Services |
| REL-001 | Set application/setup version to `1.4.0` | Release Engineer | Medium | Done | csproj files |
| REL-002 | Remove unsupported WiX `.wixproj` projects | Release / DevOps | High | Done | `GPTDeskTop.sln`, old WiX files |
| REL-003 | Add `GPTDeskTop.Publish` SDK-style project | Release / DevOps | High | Done | `src/GPTDeskTop.Publish/GPTDeskTop.Publish.csproj` |
| REL-004 | Publish app as win-x64 self-contained single-file payload | Release / DevOps | High | Done | `GPTDeskTop.Publish.csproj`, `Output/Publish` |
| REL-005 | Replace WiX installer with SDK-style `GPTDeskTop.Setup` project | Release / DevOps | High | Done | `src/GPTDeskTop.Setup/*` |
| REL-006 | Embed application payload inside Setup EXE | Release / DevOps | High | Done | `GPTDeskTop.Setup.csproj`, `Program.cs` |
| REL-007 | Install current-user app, shortcuts and uninstall registration | Release / DevOps | High | Done | `GPTDeskTop.Setup/Program.cs` |
| REL-008 | Generate final self-contained `Output/Setup/GPTDeskTop-Setup.exe` on Release build | Release / DevOps | High | Done | `GPTDeskTop.Setup.csproj` |
| REL-009 | Configure `GPTDeskTop.sln` with exactly three supported projects | Tech Lead | High | Done | `GPTDeskTop.sln` |
| DOC-001 | Document new Visual Studio Build Solution workflow | Tech Lead | Medium | Done | `README.md` |
| QA-001 | Open solution in Visual Studio and verify all three projects load without Unsupported warning | QA Engineer | High | Not Started | Solution |
| QA-002 | Build `Release | x64` and verify final Setup EXE exists | QA / Release | High | Not Started | Release pipeline |
| QA-003 | Install generated Setup EXE on clean Windows machine | QA Engineer | High | Not Started | Installer |
| QA-004 | Verify upgrade preserves `appdata.db` | QA / DBA | High | Not Started | Installer / SQLite |
| QA-005 | Verify uninstall removes application/shortcuts while preserving local database | QA Engineer | Medium | Not Started | Installer |
| QA-006 | Run 2+ tabs with different Delay and Timer values and verify isolation | QA Engineer | High | Not Started | Runtime |

## Acceptance Criteria
- `GPTDeskTop.sln` contains exactly `GPTDeskTop`, `GPTDeskTop.Publish`, and `GPTDeskTop.Setup`.
- No `.wixproj` project remains referenced by the solution.
- Visual Studio 2022 can load the three SDK-style projects without WiX/HeatWave.
- `Release | x64` Build Solution creates a self-contained application payload.
- Final standalone installer is `Output/Setup/GPTDeskTop-Setup.exe`.
- Installer creates Desktop and Start Menu shortcuts and Windows uninstall registration for the current user.
- Existing `appdata.db` is not overwritten during upgrade and is preserved by uninstall.
- Each monitor persists an independent Auto Reply, Delay (`0-300s`) and Timer (`1-60s`).

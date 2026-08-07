# GPTDeskTop Project Implementation Plan

## Current Objective
Replace the original simulated/API-oriented message monitor with a direct Chrome ChatGPT monitor. The desktop application must enumerate debuggable Chrome tabs (ID, Title, URL), let the operator select one ChatGPT conversation, detect each completed assistant response, and automatically send the text configured in the Auto Reply textbox.

## Architecture
- **UI:** WinForms `MainForm` with Chrome tab grid, auto-reply textbox, Start/Stop, live activity, and SQLite history.
- **Browser Integration:** Chrome DevTools Protocol (CDP) over the local debugging endpoint and per-tab WebSocket.
- **Monitoring:** cancellable `PeriodicTimer` loop, response-stability check, single reply per newly completed assistant message.
- **Persistence:** local SQLite `appdata.db`, auto-created on startup.
- **Isolation:** no foreground keyboard/mouse automation; DOM reads/writes happen through CDP and do not steal focus.

## Task Tracking Table

| Task ID | Task Name & Description | Suggested Role/Assignee | Priority | Status | Module / File Affected |
|---|---|---|---|---|---|
| ARC-001 | Create .NET 8 WinForms solution/project structure | Tech Lead | High | Done | `GPTDeskTop.sln`, `GPTDeskTop.csproj` |
| DB-001 | Create embedded SQLite DB and required MessageLogs/AppSettings tables automatically | Backend / DBA | High | Done | `Data/LocalDatabase.cs` |
| CHR-001 | Start a dedicated Chrome instance with remote debugging and persistent dedicated profile | Browser Integration Engineer | High | Done | `Services/ChromeDevToolsService.cs` |
| CHR-002 | Enumerate open Chrome page targets with Tab ID, Title, URL and WebSocket debugger endpoint | Browser Integration Engineer | High | Done | `Services/ChromeDevToolsService.cs` |
| CHR-003 | Read ChatGPT assistant message state through DOM/CDP without window focus | Browser Integration Engineer | High | Done | `Services/ChromeDevToolsService.cs` |
| CHR-004 | Inject auto-reply into ChatGPT prompt editor and activate Send button | Browser Integration Engineer | High | Done | `Services/ChromeDevToolsService.cs` |
| MON-001 | Poll selected tab asynchronously with `PeriodicTimer` and cancellation | Backend Engineer | High | Done | `Services/ChatGptMonitorService.cs` |
| MON-002 | Detect response completion/stability and prevent duplicate reply to same assistant output | Backend Engineer | High | Done | `Services/ChatGptMonitorService.cs` |
| MON-003 | Persist detected replies and sent auto-replies to SQLite | Backend / DBA | High | Done | `Services/ChatGptMonitorService.cs`, `Data/LocalDatabase.cs` |
| UI-001 | Add Launch Monitor Chrome and Refresh Chrome Tabs actions | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-002 | Add Chrome tabs grid containing Tab ID, Title and URL | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-003 | Add selected-target indicator and Auto Reply textbox (default `كمل`) | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-004 | Add Start/Stop monitoring state and thread-safe live activity updates | UI / Backend | High | Done | `UI/MainForm.cs` |
| UI-005 | Keep persisted history grid and newest-first refresh | UI Developer | Medium | Done | `UI/MainForm.cs` |
| CFG-001 | Add Chrome/CDP and polling configuration | Backend Engineer | Medium | Done | `Configuration/AppConfig.cs`, `appsettings.json` |
| DOC-001 | Document Chrome debugging requirement and run workflow | Tech Lead | Medium | Done | `README.md`, `TaskPlanner.md` |
| QA-001 | Build with .NET 8 SDK on Windows | QA Engineer | High | Not Started | Whole solution |
| QA-002 | Validate current ChatGPT DOM selectors against live site | QA / Browser Integration | High | Not Started | `ChromeDevToolsService.cs` |
| QA-003 | Verify repeated `كمل` loop and Stop behavior across long-running conversation | QA Engineer | High | Not Started | Runtime |
| FUT-001 | Optional Chrome Extension/native messaging mode for attaching to the user's ordinary Chrome profile | Browser Integration Engineer | Medium | Not Started | Future extension module |

## Acceptance Criteria
- Refresh Chrome Tabs displays ID, Title and URL for every debuggable page target.
- User can select a ChatGPT tab explicitly.
- Start Monitoring does not freeze or focus-steal from other applications.
- Existing assistant content at monitor startup does not immediately trigger a reply.
- A newly completed assistant reply triggers exactly one configured auto-reply.
- The same assistant response is not handled twice.
- Stop Monitoring cancels the loop gracefully.
- Browser/DOM/database failures appear in Live Activity rather than crashing the process.
- Every detected reply and automatic send is written to local SQLite history.

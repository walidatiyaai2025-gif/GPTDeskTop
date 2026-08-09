# GPTDeskTop Project Implementation Plan

## Current Objective
Maintain GPTDeskTop as a persistent .NET 8 multi-tab ChatGPT monitor with independent per-tab timing, SQLite persistence, tray notifications, automatic timeout/crash recovery, Chrome lifecycle control, Fluent/WinUI-inspired WinForms UX, persistent exception diagnostics, and a Visual Studio-buildable standalone Setup EXE.

## Architecture
- **Application:** .NET 8 WinForms, version `1.8.0`.
- **Browser:** dedicated Chrome profile controlled through Chrome DevTools Protocol.
- **Saved Monitors:** independent Auto Reply, Reply Delay, Monitor Timer and Enabled state per tab.
- **Runtime:** one cancellable worker per Monitor ID.
- **Development Task Engine:** one cancellable worker per engine instance; state and message index persist across restart; Cooling state resumes without creating a duplicate worker. Worker cancellation is awaited before restart, stop or disposal so runtime files/resources cannot outlive the owning engine.
- **Development Message Hot Reload:** the message catalog is read with Windows-safe read/write/delete sharing and short retry on transient I/O/JSON replacement windows, allowing atomic catalog edits during Cooling without restarting the engine.
- **Persistence:** SQLite `appdata.db` with monitor configuration, defaults, history, crash counters and exception records; development-task state uses a dedicated JSON state file.
- **Transient CDP Recovery:** `Promise was collected` from `Runtime.evaluate` is retried internally up to three times before being treated as a real monitor failure; transient monitor-boundary retries do not create repeated crash diagnostics.
- **Exception Diagnostics:** unhandled UI/domain/task exceptions and monitor exceptions are written to `MessageLogs` and `logs/exceptions-YYYYMMDD.log`.
- **No-response Watchdog:** global `NoResponseRefreshSeconds`, default `180` seconds; only the affected tab is refreshed when no new assistant response arrives within the configured period. A Windows/Chrome integration probe runs two real monitor workers at a 30-second timeout and verifies that only the stale tab reloads while the active tab remains untouched.
- **Crash Detection:** `LastShutdownClean` is set to `0` for the running process and changed to `1` only after a graceful MainForm close. A subsequent startup seeing `0` increments `CrashCount` and schedules full session recovery. A Windows CI probe launches the real `GPTDeskTop.exe`, force-kills it, relaunches it against the same SQLite database, and verifies the unclean-start state transition.
- **Crash Session Recovery:** close leftover monitor tabs, reopen all saved monitor URLs, send the configured recovery message (default `كمل`), update saved Tab IDs, then restart enabled monitor workers. Recovery orchestration is routed through `ICrashRecoveryRuntime`; SQLite-backed integration tests verify all-recipient delivery, enabled-only restart, partial-failure persistence, and idempotent retry without resending already verified monitors.
- **Fatal Auto Restart:** a fatal exception escaping the app attempts one automatic restart with a 30-second loop guard.
- **Notifications:** Windows tray balloons with configurable duration and sound.
- **Delivery Timeout Recovery:** save timeout, create a new ChatGPT tab, send recovery message, move the existing Monitor ID to the new tab, continue monitoring, close old timed-out tab.
- **Conversation Context Rotation:** when ChatGPT reports the conversation/context limit, create a new chat and verify the handoff before moving the Monitor ID. If the new composer is temporarily unavailable, retry with one new-tab-only reload; if delivery is still unverified, close only the unused new tab, preserve the old chat and leave the same limit response eligible for a later rotation retry.
- **Chrome Lifecycle:** CDP-first minimize/hide/show behavior while workers continue; close monitor tabs on exit. Hidden-window CDP is covered by a real Chrome smoke probe on every push plus a completed 610-second acceptance endurance run.
- **UX:** Fluent/WinUI-inspired WinForms styling, right-click context menus, green/red runtime lamp, Crash Count card and Monitor Count card. Lamp/card values are centralized in `HomeMetricsPresentation` and covered by deterministic runtime tests while `HomeMetricsService` remains responsible for WinForms binding.
- **Release pipeline:** application -> publish -> standalone setup, all SDK-style Visual Studio projects. A full `Release | x64` solution build serializes Setup after the application through a solution dependency; `GPTDeskTop.Build` suppresses its packaging side effect during full solution builds, preventing concurrent publish races while preserving **Build Solution -> `Output\Setup\GPTDeskTop-Setup.exe`**.

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
| MON-006 | Make conversation-limit rotation handoff retryable and keep the old chat until verified delivery succeeds | Browser / Backend | High | Done | `ChatGptMonitorService.cs`, `ChromeDevToolsService.cs` |
| CRASH-001 | Detect unclean shutdown and increment persistent crash count | Backend Engineer | High | Done | `Program.cs` |
| CRASH-002 | Auto restart once after fatal crash with restart-loop guard | Backend Engineer | High | Done | `Program.cs` |
| CRASH-003 | Reopen all saved monitor tabs and send recovery message after crash | Browser / Backend | High | Done | `CrashRecoveryService.cs` |
| CRASH-004 | Rebind saved monitors to recreated Chrome Tab IDs and restart enabled workers | Backend Engineer | High | Done | `CrashRecoveryService.cs` |
| CRASH-005 | Provide process-level force-kill/relaunch QA probe and deterministic recovery runtime adapter | Backend / QA | High | Done | `CrashRecoveryProcessProbe.cs`, `ICrashRecoveryRuntime.cs`, crash QA workflow/tests |
| CHR-001 | Improve Hide/Show using CDP window state plus native hide fallback | Browser Integration | High | Done | `ChromeDevToolsService.cs` |
| CHR-002 | Close monitor tabs when application exits | Browser Integration | High | Done | Main/Chrome services |
| UI-001 | Fluent/WinUI-inspired WinForms visual system | UI Developer | High | Done | `FluentTheme.cs`, forms |
| UI-002 | Right-click menus for Open Tabs / Saved Monitors / History | UI Developer | Medium | Done | `MainForm.cs` |
| UI-003 | Green/red runtime lamp in monitor Status column | UI Developer | Medium | Done | `HomeMetricsService.cs` |
| UI-004 | Add Crash Count and Monitor Count home cards | UI Developer | Medium | Done | `HomeMetricsService.cs` |
| UI-005 | Add no-response timeout field to Settings | UI / Backend | High | Done | `SettingsForm.cs` |
| NOT-001 | Configurable balloon duration and sound | UI / Backend | High | Done | Notification/Settings services |
| DEV-001 | Prevent duplicate development-task workers; persist state and safely resume Working/Cooling after restart | Backend Engineer | High | Done | `DevelopmentTaskEngine.cs`, `DevelopmentTaskState.cs` |
| DEV-002 | Add regression coverage for Cooling persistence/resume and worker lifecycle | QA / Backend | High | Done | `tests/GPTDeskTop.RuntimeTests/DevelopmentTaskEngineTests.cs` |
| DEV-003 | Await worker shutdown before restart/stop/dispose and support concurrent atomic message-catalog hot reload | Backend / Runtime | High | Done | `DevelopmentTaskEngine.cs`, message reload tests |
| CI-001 | Run runtime automation tests before application/setup/helper builds | DevOps / QA | High | Done | `.github/workflows/build.yml` |
| CI-002 | Validate `Release | x64` solution builds without concurrent Setup/Build publish races | DevOps / Release | High | Done | `.github/workflows/qa-release-x64.yml`, Build/Setup projects |
| CI-003 | Serialize full-solution Setup packaging behind the application dependency and verify Build Solution emits the standalone Setup | DevOps / Release | High | Done | `GPTDeskTop.sln`, `GPTDeskTop.Setup.csproj`, `qa-release-x64.yml` |
| REL-001 | Synchronize application, publish and setup metadata to `1.8.0` | Release Engineer | High | Done | `GPTDeskTop.csproj`, setup/build projects |
| REL-002 | Establish validated v1.8.0 release-readiness baseline without creating a tag/release | Release Engineer / QA | High | Done | Main CI, Release x64 CI, QA workflows, documentation |
| QA-001 | Build `Release | x64` in Visual Studio-compatible solution configuration and verify all three project outputs | QA Engineer | High | Automated | `qa-release-x64.yml`, whole solution |
| QA-002 | Verify `Promise was collected` transient retry and confirm retry attempts do not create repeated crash diagnostics | QA Engineer | High | Automated | `ChromeTransientFailureRegressionTests.cs`, Chrome integration |
| QA-003 | Force-kill the real GPTDeskTop process, relaunch against the same SQLite DB, and verify `CrashCount`, pending recovery and recovery identity | QA Engineer | High | Automated | `CrashRecoveryProcessProbe.cs`, `qa-crash-process.yml` |
| QA-004 | Verify every recreated recovery tab receives the configured recovery message, enabled monitors restart, partial failure stays pending, and retries do not resend verified monitors | QA Engineer | High | Automated | `ICrashRecoveryRuntime.cs`, `CrashRecoveryOrchestrationTests.cs` |
| QA-005 | Run monitor hidden for 10+ minutes and verify CDP polling continues | QA Engineer | High | Automated | `HiddenChromeProcessProbe.cs`, `qa-hidden-chrome.yml` |
| QA-006 | Set no-response timeout to 30 seconds and verify exactly one affected tab refreshes | QA Engineer | High | Automated | `NoResponseWatchdogProcessProbe.cs`, `qa-no-response-watchdog.yml` |
| QA-007 | Verify green lamp while monitor runs, red lamp when stopped, and home cards update correctly | QA Engineer | Medium | Automated | `HomeMetricsPresentation.cs`, `HomeMetricsPresentationTests.cs` |
| QA-008 | Verify development task engine survives restart while Working and Cooling without duplicate MessageReady events | QA Engineer | High | Automated | Runtime automation tests |
| QA-009 | Lock conversation-limit rotation retry behavior, new-chat-only recovery reload, and deferred recovery send semantics with regression tests | QA / Backend | High | Automated | `ChatGptRotationHandoffRegressionTests.cs` |

## CI Validation
The release-readiness baseline is fully green. Commit `a8761668` passed the complete main `Build GPTDeskTop` workflow: runtime automation tests, lifecycle/delivery/multi-monitor/rebinding/CDP/crash-recovery invariants, application build, standalone Setup build, helper build and rotation-safety checks. The dedicated `QA Release x64` workflow on the same commit also passed, including the full `Release | x64` solution build, solution dependency validation, and explicit verification of `Output\Setup\GPTDeskTop-Setup.exe` plus its `GPTDeskTop Setup v1.8.0` version receipt.

QA-005 also completed successfully on the dedicated real Windows/Chrome/CDP endurance run from commit `d87fcacf`: **610.6930764 seconds**, **606 successful hidden-window CDP polls**, **0 failed polls**, `HideChanged=True`, `ShowChanged=True`, with every successful poll returning the expected monitored content. The regular push workflow has been restored to a 30-second hidden-Chrome smoke while `workflow_dispatch` retains the 610-second endurance option.

Other validated gates include force-kill/relaunch crash recovery, exact per-tab 30-second no-response isolation, persisted schedule/target identity across restart, home metrics presentation, message hot reload, delivery receipts, and development-task recovery.

## Acceptance Criteria
- Transient `Promise was collected` errors are retried automatically and do not flood the exception history.
- Every real exception remains visible in Stored History and the exception log file.
- `NoResponseRefreshSeconds` defaults to `180` and remains editable in seconds.
- With `NoResponseRefreshSeconds=30`, a stale monitor tab refreshes exactly once while an independently active monitor tab is not refreshed, and both monitor workers continue running.
- A force-killed `GPTDeskTop.exe` leaves `LastShutdownClean=0`; the next launch detects the unclean shutdown, increments `CrashCount`, sets `CrashRecoveryPending=1`, and creates a non-empty recovery identity.
- After an unclean shutdown, GPTDeskTop closes leftover monitor tabs, recreates saved tabs, sends the configured recovery message and restarts enabled monitors.
- Crash recovery persists per-monitor verified delivery so a partial recovery retry never sends the same recovery message twice to an already verified monitor.
- Crash recovery clears its pending incident only after all saved monitors have a successful recovery outcome.
- Fatal crashes attempt one automatic restart without entering a restart loop.
- Hidden Chrome does not stop production CDP polling; the completed acceptance receipt is 610.693 seconds with 606 successful polls and zero failures.
- Saved Monitors map Running to a green bold `● Running` lamp and Stopped to a red bold `● Stopped` lamp.
- Home displays persistent Crash Count and live/total Monitor Count cards from the shared presentation model.
- Development task engine never starts a second uncancellable worker from `AdvanceAsync`.
- Development task engine persists CurrentMessageIndex and Cooling state and resumes safely after process restart.
- Development task workers are fully terminated before restart, stop or disposal completes.
- The development message catalog can be atomically edited during Cooling without Windows file-lock failures and the next work window sees the updated catalog.
- A conversation-limit rotation closes the old chat only after the handoff is verified in the new chat.
- A temporary new-chat composer failure never permanently consumes the conversation-limit response; the rotation remains eligible for a later retry.
- New-chat handoff recovery may reload the newly-created tab once, while ordinary Auto Reply delivery never uses that recovery reload path.
- `Release | x64` builds all three solution projects without concurrent publish races and produces `Output\Setup\GPTDeskTop-Setup.exe` from Build Solution.
- Application, publish helper and standalone Setup metadata report the same release version `1.8.0`.
- `GPTDeskTop.sln` remains composed of exactly three supported SDK-style projects.
# UI Startup / Dead UI Audit

Issue: #275 (`UISTART-001..012`)

## Cold-start construction currently happening before `Application.Run`

`Program.Main` constructs the following operator UI before the main message loop starts:

- `MainForm`
- `DevelopmentTaskDashboardControl`
- `RuntimeHealthControl`
- `SupportDiagnosticsControl`
- `HistoryWorkspaceControl`
- `HomeMetricsService`

The four secondary controls above are added to `MainForm` before the first paint. `SupportDiagnosticsControl` is constructed even when Runtime Health is collapsed/hidden. `HistoryWorkspaceControl` is constructed even when its persisted state is collapsed. These are priority lazy-load candidates.

## Runtime-critical objects that must remain eager

The following are not dead UI and must remain available for recovery/monitor continuity:

- `LocalDatabase`
- `ChromeDevToolsService`
- `ChatGptMonitorService`
- `TrayNotificationService`
- crash recovery / last-working-state / instance-handoff primitives
- `DevelopmentTaskRuntimeBinding` (runtime state may need automatic resume)

The UI for those services can still be lazy even when the service/runtime object must remain eager.

## Duplicate / legacy UI paths found

- `MainForm` still owns the legacy toolbar commands: New Chat + Monitor, Add Monitor, Edit Monitor, Delete, Start/Stop Selected, Start/Stop All.
- `ProjectMonitorUiBootstrap` adds a separate `Projects` button later through an `Application.Idle` scan.
- `CompactTopCommandMenuExperience` builds another command surface by proxying existing buttons.
- `ProjectsHubNavigationConsolidation` performs a later `Application.Idle` scan to remove the legacy Monitors menu and insert `Projects Hub`.

The resulting behavior is correct but the architecture creates controls first, then discovers/mutates/hides them later. This is unnecessary startup/control-tree work.

## Settings / GitHub findings

- `SettingsForm` itself is created only when the user invokes Settings; it is not constructed by `Program.Main`.
- GitHub integration UI is created from the Projects/Git Settings path and its `LoadAsync` runs from the dialog `Shown` event. It is not required for cold startup.
- Therefore the target is to preserve this lazy behavior and prevent future startup regressions.

## Obsolete startup mutation pattern

Three current helpers use module initialization plus `Application.Idle` scanning/mutation:

1. `ProjectMonitorUiBootstrap`
2. `ProjectsHubNavigationConsolidation`
3. `CompactTopCommandMenuExperience`

These should move toward explicit one-owner installation and must not remain permanent Idle scanners.

## Implementation order

1. Stop repeated Idle scanning and remove provably redundant post-startup UI mutation.
2. Make Projects Hub the sole visible project/monitor surface while preserving runtime commands internally only where recovery/tests still require them.
3. Lazy-create Runtime Health diagnostics, Support Diagnostics and History UI on first operator activation.
4. Keep development runtime eager only if recovery requires it; defer its dashboard control until after first paint / explicit open.
5. Add regression tests around cold-start construction and duplicate hook registration.

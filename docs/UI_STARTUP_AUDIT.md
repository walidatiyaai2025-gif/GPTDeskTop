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

The four secondary controls above are added to `MainForm` before the first paint. `SupportDiagnosticsControl` is constructed even when Runtime Health is collapsed/hidden. `HistoryWorkspaceControl` is constructed even when its persisted state is collapsed. These remain the priority lazy-load candidates for UISTART-008/010.

## Runtime-critical objects that must remain eager

The following are not dead UI and must remain available for recovery/monitor continuity:

- `LocalDatabase`
- `ChromeDevToolsService`
- `ChatGptMonitorService`
- `TrayNotificationService`
- crash recovery / last-working-state / instance-handoff primitives
- `DevelopmentTaskRuntimeBinding` when runtime state may need automatic resume

The UI for those services can still be lazy even when the runtime object must remain eager.

## Canonical Projects location

The project-monitor surface is **Projects Hub**.

- Main window: **Projects** beside **Settings** while the legacy toolbar is visible.
- Compact operator layout: **☰ Commands → Projects Hub**.
- Inside Projects Hub: project rows, runtime state/results/tasks and **New Project Monitor**.

`ProjectMonitorUiBootstrap` is now the single owner for both Projects entry points and the lazy dashboard lifetime. The separate `ProjectsHubNavigationConsolidation` helper has been deleted.

## Navigation cleanup completed

- The old compact-menu `Monitors` group is removed after the canonical Projects entry is available.
- The duplicate `ProjectsHubNavigationConsolidation` ModuleInitializer/Application.Idle scanner is removed.
- One Projects bootstrap remains temporarily; its Idle hook detaches immediately after successful one-time installation.
- Legacy MainForm monitor commands remain internal compatibility/runtime behavior until the final native-Projects refactor proves recovery/tests do not need the controls.

## Lazy contracts verified

### Application Settings
`SettingsForm` is created only from `MainForm.OpenSettingsAsync`; it is not constructed by `Program.Main`.

### Projects Hub
`ProjectMonitorDashboardForm` is constructed only inside `ProjectMonitorUiBootstrap.ShowProjectsHub` after an operator invokes Projects. `Program.Main` does not instantiate it.

### GitHub UI
`GitHubIntegrationControl` is constructed only inside `ProjectMonitorUiBootstrap.ShowGitSettings`, which is reached only when first-time/missing/invalid repository credentials require operator action. Silent GitHub preflight uses stored credentials without constructing GitHub UI.

## Remaining startup/UI work

1. Defer Development Plan / Runtime Health / Support / History visual controls until first operator activation where runtime contracts permit it.
2. Remove the final Projects `ModuleInitializer`/`Application.Idle` injection by making Projects a native MainForm command.
3. Add cold-start regression/performance instrumentation proving heavy secondary forms/services are not instantiated during MainForm construction and event hooks are registered once.
4. Run full recovery/runtime/release CI before merge.

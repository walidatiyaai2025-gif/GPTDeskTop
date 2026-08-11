# Development Runtime UI Status

## Current Task

- No active tracked runtime/UI task after UI-POLISH-006 verification. The next change should come from a tracked feature/fix issue or an explicit release operation.

## Completed

- **UI-POLISH-006 — Main window dock order and action visibility:** merged via PR #185. The primary `DockStyle.Fill` workspace now reserves real space for Development Plan, Runtime Health, Support Diagnostics and Stored History instead of being painted underneath those edge-docked surfaces. Main, development, runtime-health and history actions use DPI-aware text-safe minimum widths, and compact Runtime Health breakpoints hide secondary summary/time text before action columns can truncate. Final PR head `4219de12324d724189bc53849df47de426fd2039` passed the complete 8/8 gate set and was squash-merged to `main` as `c44c6d79fdab2f92b4d7e0bd0037daebc843d16a`.
- **UI-007 / Issue #112 — MainForm runtime-status font resource lifecycle:** merged via PR #115. The Saved Monitors Runtime-column formatter now reuses one MainForm-owned bold status font instead of allocating a GDI-backed `Font` on every formatting pass; the font is disposed during completed MainForm shutdown. Final PR head `a132cc285edb4856916ef0d4da6a2c597e595749` and main implementation commit `f4f87021dd6f9d9cde99a2d4af1c05e883bb4a24` passed the complete 8/8 gate set, and stable build `f4f87021` was published successfully.
- **UI-006 / Issue #105 — Home status font resource lifecycle:** merged via PR #106. Saved Monitor Running/Stopped formatting now reuses owned cached fonts instead of allocating a new GDI-backed `Font` on every formatting pass; the cache is disposed with `HomeMetricsService`. Final PR head and main implementation commit both passed the complete 8/8 gate set, and stable build `8a0ebe2e` was published successfully.
- Production `DevelopmentTaskRuntimeBinding` owns the development engine and dynamic saved-monitor delivery coordinator.
- Dashboard Start/Pause/Resume/Stop controls invoke the production runtime binding rather than a standalone engine.
- Dashboard status/countdown/receipt data is sourced from the bound production engine state.
- Dashboard displays current message position, last monitor/tab identity, verified-delivery index, receipt count, revision and Work/Cooling countdown.
- Program creates the saved-monitor target factory, runtime binding, and dashboard before showing the main application.
- Runtime binding is disposed during application shutdown.
- Editable development-message catalog is available directly from the Dashboard through the **Messages** control.
- Persisted Work/Cooling settings are available directly from the Dashboard through the **Schedule** control.
- Schedule edits apply to the next lifecycle window and are reloaded by the engine rather than mutating an active window.
- Restart persistence is covered for both schedule settings and recreated Chrome target IDs.
- Home monitor presentation is centralized and covered by tests for green Running lamp, red Stopped lamp, Crash Count and live/total Monitor Count.
- Real Chrome/CDP QA verifies hidden-window polling and per-tab no-response isolation.
- CI verifies the production wiring, lifecycle contract, persistence, delivery invariants and application/setup/helper builds.

## Delivery Guarantees Preserved

- One development-plan message per configured Work window.
- Configured Cooling window with no delivery.
- Verified CDP send before task advancement.
- Per-monitor delivery receipts.
- Dynamic saved-monitor/tab rebinding after Cooling or restart.
- Recreated target IDs persist and become the exact identity on the next resolution.
- Start/Pause/Resume/Stop lifecycle persistence.
- Worker shutdown completes before engine resources are released.
- Main-window presentation changes do not mutate monitor, recovery, CDP, task-engine or persistence semantics.

## QA Status

The v1.8.0 runtime/UI baseline remains release-ready. UI-POLISH-006 passed Build GPTDeskTop #605, QA Release x64 #393, QA Hidden Chrome CDP #375, QA Passive Chat Wait #369, QA Crash Process Recovery #383, Development Delivery Receipts #483, Development Task Recovery #479 and Development Message Reload #310 on the exact final PR head before merge.

The dedicated QA-005 real Windows/Chrome/CDP hidden-window endurance run also remains valid: **610.6930764 seconds**, **606 successful polls and zero failures**, while every-push CI retains the shorter 30-second smoke test.

UI-006, UI-007 and UI-POLISH-006 are completed and verified. Further changes should continue to be driven by tracked feature/fix tasks or an explicit release operation, not by repeating completed runtime UI gates.

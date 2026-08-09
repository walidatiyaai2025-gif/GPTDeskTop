# GPTDeskTop Development Status

## Current Focus
Release-readiness validation for the completed development-plan runtime, multi-monitor continuity, crash recovery, Chrome/CDP resilience, and operator-configurable scheduling.

## Confirmed Complete

- Crash startup/clean-shutdown state tracking.
- Process-level force-kill/relaunch verification against the same SQLite database.
- Crash recovery pending marker and recovery incident identity.
- Per-monitor crash-recovery idempotency markers.
- Partial crash-recovery handling: failed monitors remain pending; successful monitors are not recovered twice.
- CDP `Promise was collected` bounded retry handling without repeated crash diagnostics.
- Verified ChatGPT message delivery using before/after user-message snapshots.
- Editable development-message catalog with 10 distinct messages and an extensible catalog format.
- Exactly one development-plan message is emitted per work window; the remaining time is idle rather than sending the other catalog variants.
- Cooling window with no ChatGPT delivery.
- Single-emission guard for the current development message.
- Delivery checkpoint containing monitor, tab, message index, and fingerprint.
- Restart recovery of the persisted development-task position.
- Idempotent Start/Resume lifecycle for the development engine.
- Worker cancellation is awaited before restart, stop and disposal.
- Multi-monitor development delivery coordinator.
- Per-monitor delivery receipts so partial delivery retries only unresolved recipients.
- Exact saved Chrome target rebinding by persisted DevTools target ID.
- Safe restart rebinding by persisted ChatGPT conversation URL when Chrome assigns a new target ID.
- Recreated target IDs are persisted in SQLite and become the exact `PersistedTabId` on the next resolution/restart.
- No title-based fallback for chat identity.
- Recipient factory that rebuilds enabled development targets from the persisted monitor registry.
- Missing-chat handling that leaves the recipient unresolved and records `DevelopmentMonitorTabUnavailable`.
- Rebind telemetry via `DevelopmentMonitorRebound`.
- Dynamic recipient resolution immediately before each emitted development message.
- Runtime binding that attaches dynamic delivery before Start/Resume, so the first message cannot bypass delivery.
- Safe persistence of a recreated Chrome target ID only after exact conversation-URL rebinding.
- Target-update telemetry via `DevelopmentMonitorTargetIdUpdated`.
- Dashboard lifecycle controls: Start, Pause, Resume, Stop, current message, recipient/receipt state, and Work/Cooling countdown.
- Editable message catalog UI: Add, Update, Remove, Move Up, Move Down, and atomic Save.
- Persisted operator-configurable Work/Cooling schedule settings with validation and atomic Save.
- Persisted schedule values are loaded by a new engine instance after process-style restart.
- Schedule settings UI exposed from the development dashboard.
- Engine reloads the persisted schedule at Start/Resume and at the beginning of the next Work window after Cooling, so changing settings does not mutate an already-running window.
- Development message catalog supports safe concurrent atomic hot reload while the engine is running.
- Real Windows/Chrome watchdog integration verifies a 30-second stale-tab refresh does not refresh an independently active tab.
- Home status presentation has deterministic tests for green Running lamp, red Stopped lamp, persistent Crash Count, and live/total Monitor Count.
- CI gates for catalog, schedule settings, delivery, work-window lifecycle, CDP reliability, crash recovery, multi-monitor delivery, dynamic runtime binding, saved-chat rebinding, Release|x64, force-kill recovery, no-response isolation and hidden-Chrome CDP smoke.

## Current Gate

Commit `05502285` passed the complete main runtime/build pipeline after adding explicit process-restart persistence tests for schedule settings and recreated Chrome target identity. The only acceptance run still open from the current QA board is **QA-005**, a dedicated 610-second real Chrome/CDP hidden-window endurance run. Every-push hidden-Chrome smoke remains 30 seconds; the long run is a one-time acceptance gate, not a permanent 10-minute cost on every commit.

## Next Executable Tasks

1. Complete the running QA-005 610-second hidden Chrome/CDP endurance gate and record its receipt.
2. Reconcile release/build documentation with the now race-safe solution build: full `Release | x64` compiles all three projects, while standalone Setup packaging is intentionally performed by building the Setup project directly.
3. After all QA gates are green, record a release-readiness baseline and verify the final standalone Setup output path/version before any tag or release operation.

## Non-Negotiable Delivery Rules

- Never advance a development message before verified delivery.
- Never send the same message twice to a recipient when a persisted receipt proves it was already delivered to the same monitor/tab/message/fingerprint.
- A multi-monitor message advances only when every active recipient has a verified receipt.
- Start is idempotent: one engine lifecycle/worker only.
- Each Work window emits at most one catalog message.
- Default schedule is 10 minutes Working and 5 minutes Cooling.
- Schedule changes apply to the next Work/Cooling cycle, not an already-running window.
- Cooling never authorizes ChatGPT delivery.
- Recovery must prefer the existing saved Chrome tab and Monitor ID.
- A recreated Chrome target may update the saved target ID only after its conversation URL matches exactly.
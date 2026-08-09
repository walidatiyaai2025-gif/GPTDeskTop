# GPTDeskTop Development Status

## Current Focus
Development-plan automation, verified ChatGPT delivery, restart recovery, and multi-monitor continuity.

## Confirmed Complete

- Crash startup/clean-shutdown state tracking.
- Crash recovery pending marker and recovery incident identity.
- Per-monitor crash-recovery idempotency markers.
- Partial crash-recovery handling: failed monitors remain pending; successful monitors are not recovered twice.
- CDP `Promise was collected` bounded retry handling.
- Verified ChatGPT message delivery using before/after user-message snapshots.
- Editable development-message catalog with 10 distinct messages and an extensible catalog format.
- Exactly one development-plan message is emitted per 10-minute work window; the remaining time is idle rather than sending the other catalog variants.
- 5-minute cooling window with no ChatGPT delivery.
- Single-emission guard for the current development message.
- Delivery checkpoint containing monitor, tab, message index, and fingerprint.
- Restart recovery of the persisted development-task position.
- Idempotent Start/Resume lifecycle for the development engine.
- Multi-monitor development delivery coordinator.
- Per-monitor delivery receipts so partial delivery retries only unresolved recipients.
- Exact saved Chrome target rebinding by persisted DevTools target ID.
- Safe restart rebinding by persisted ChatGPT conversation URL when Chrome assigns a new target ID.
- No title-based fallback for chat identity.
- Recipient factory that rebuilds enabled development targets from the persisted monitor registry.
- Missing-chat handling that leaves the recipient unresolved and records `DevelopmentMonitorTabUnavailable`.
- Rebind telemetry via `DevelopmentMonitorRebound`.
- Dynamic recipient resolution immediately before each emitted development message.
- Runtime binding that attaches dynamic delivery before Start/Resume, so the first message cannot bypass delivery.
- Safe persistence of a recreated Chrome target ID only after exact conversation-URL rebinding.
- Target-update telemetry via `DevelopmentMonitorTargetIdUpdated`.
- Regression coverage for URL rebinding, title-fallback rejection, one-message-per-window, and restart duplicate prevention.
- CI gates for catalog, delivery, work-window lifecycle, CDP reliability, crash recovery, multi-monitor delivery, dynamic runtime binding, and saved-chat rebinding.

## Current Gate

The latest source changes are committed to `main`. The one-message-per-work-window rule and extensible catalog validation are implemented and CI-gated. The latest commits have not yet produced an observed GitHub Actions run in this session. Do not mark the build green until an actual workflow run completes successfully.

## Next Executable Tasks

1. Verify persisted target-ID update survives a process restart and is used as the exact target on the next resolution.
2. Run the complete CI/build pipeline and fix any real compile/test failures before adding new architecture.
3. Add UI controls for Start, Stop, Pause/Resume, current message, Work/Cooling status, recipient health, and delivery receipts.
4. Add release-readiness packaging only after CI is green.

## Non-Negotiable Delivery Rules

- Never advance a development message before verified delivery.
- Never send the same message twice to a recipient when a persisted receipt proves it was already delivered to the same monitor/tab/message/fingerprint.
- A multi-monitor message advances only when every active recipient has a verified receipt.
- Start is idempotent: one engine lifecycle/worker only.
- Each work window emits at most one catalog message.
- Cooling is 5 minutes by default; Working is 10 minutes by default.
- Recovery must prefer the existing saved Chrome tab and Monitor ID.
- A recreated Chrome target may update the saved target ID only after its conversation URL matches exactly.

# GPTDeskTop Development Status

## Current Focus
The v1.8.0 release-readiness baseline remains intact. Post-1.8 maintenance is extending operator UX, supportability, portability and monitor-identity safety without changing the validated monitor/recovery/delivery architecture. The searchable Stored History Explorer, read-only Runtime Health / Connection Center, privacy-safe Support Diagnostics Bundle, portable Configuration Backup Export, transactional Configuration Backup Restore/Import, and Stable Monitor Conversation Identity Guard are merged on `main`. No tag or GitHub release has been created by this work.

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
- Real Windows/Chrome hidden-window endurance acceptance completed for 610.6930764 seconds with 606 successful CDP polls and zero failures.
- Full `Release | x64` solution build serializes Setup after the application and produces `Output\Setup\GPTDeskTop-Setup.exe` without concurrent publish races.
- CI gates for catalog, schedule settings, delivery, work-window lifecycle, CDP reliability, crash recovery, multi-monitor delivery, dynamic runtime binding, saved-chat rebinding, Release|x64, force-kill recovery, no-response isolation, hidden-Chrome CDP and rotation safety.
- Main-window shutdown is asynchronous and bounded so closing with the window `X` cannot deadlock the WinForms UI thread while monitor/Chrome cleanup runs.
- Main window, split ratios and Development Plan expansion state persist safely across restart and recover to a visible screen when the previous monitor is unavailable.
- Settings and Monitor Settings dialogs are DPI-safe, keyboard/accessibility aware, validation-safe, and protected from duplicate async save operations.
- Message-count conversation rotation can open a new ChatGPT conversation after a configurable assistant-message threshold and send the configured fixed start message while preserving the same Monitor ID.
- Searchable Stored History Explorer is merged on `main` (`d3f061293937270cfd41975bb12e119455207cfe`): client-side search/filtering, visible/total counts, explicit no-match state, copy, CSV export, persisted expansion and accessibility coverage.
- Runtime Health / Connection Center is merged on `main` (`c27da0a09afe8f157d50125f7ce5e8a14ce09126`): read-only Chrome/CDP and SQLite probes, ChatGPT tab count, saved/running monitor count, Healthy/Degraded/Unavailable presentation, five-second bounded refresh, duplicate-refresh protection, persisted expansion and accessibility coverage.
- Privacy-safe Support Diagnostics Bundle is merged on `main` (`54846f3338eeeeec196e7d881813563d0080d2a3`): user-selected atomic ZIP export containing sanitized runtime/configuration metadata, Chrome/CDP and SQLite health/counts, history direction/status aggregates and exception-file metadata while explicitly excluding conversation content, monitor titles/URLs, message text, raw SQLite contents, raw exception contents and local machine/user identity.
- Portable Configuration Backup Export is merged on `main` (`926fe75213550ec181d72215c49d837f626e281a`): Settings > Backup & Portability can export schema 1.0 JSON with an explicit allowlist of DB-backed operator settings plus saved monitor configuration. The export uses a same-directory temporary file and final replace/move, excludes history/raw SQLite/runtime IDs/rotation counters/crash state/UI state/logs/machine identity, and explicitly warns that the backup is sensitive because it can contain conversation URLs and configured message text.
- Transactional Configuration Backup Restore/Import is merged on `main` (`590a07e0af8e51b63f318c87576ecc5ae6d51354`): schema 1.0 files are strictly validated before mutation; allowlisted settings and monitor definitions are applied in one SQLite transaction; monitor merges use exact conversation URL and preserve local SQLite Id, runtime TabId, RotationCount and history identity; missing monitors are inserted with fresh local identity; local-only monitors are never deleted; ambiguous duplicate local URLs roll back the whole import; runtime/history/crash/UI/log/machine state is never imported; operator confirmation and an application restart are required before relying on imported configuration.
- Stable Monitor Conversation Identity Guard is merged on `main` (`08342cfe4f17627351c391a9cbd51d9636e095b6`): a shared HTTPS ChatGPT `/c/{conversation-id}` identity validator is distinct from the broader ChatGPT-host detector; the operator-facing Chrome list exposes only stable conversation tabs; Add Monitor and Start Monitor defensively reject generic/non-conversation pages; schema 1.0 import uses the same guard; nested GPT conversation paths and legacy `chat.openai.com/c/...` remain supported; generic Chrome tab enumeration and exact raw conversation-URL persistence/matching semantics remain unchanged.

## Release-Readiness Baseline

- **Functional/main baseline:** commit `a8761668` passed the complete `Build GPTDeskTop` workflow, including runtime automation, all invariant checks, application build, Setup build, Build helper and rotation safety.
- **Visual Studio-compatible build baseline:** the `QA Release x64` workflow on `a8761668` passed the full solution build and explicitly verified `Output\Setup\GPTDeskTop-Setup.exe` plus the `GPTDeskTop Setup v1.8.0` version receipt.
- **Hidden Chrome endurance receipt:** commit `d87fcacf` completed 610.6930764 seconds, 606 successful polls, zero failed polls, `HideChanged=True`, `ShowChanged=True`.
- **Restart persistence baseline:** commit `05502285` passed explicit process-style persistence tests for schedule settings and recreated Chrome target identity.
- **No-response isolation:** real two-monitor Chrome/CDP integration verified exactly one stale-tab refresh and zero active-tab refreshes with a 30-second timeout.
- **Post-1.8 Runtime Health validation:** PR #32 head `ba8965cb9d8fdcf32a95c1ec034a606b31804254` passed Build GPTDeskTop #285, QA Release x64 #73, QA Hidden Chrome CDP #55, QA Crash Process Recovery #63, QA No-Response Watchdog #49, Development Delivery Receipts #163, Development Task Recovery #159 and Development Message Reload #33 before merge.
- **Post-1.8 Support Bundle validation:** PR #34 head `09e18196de5e8aeba21bd7bdefc4b6e54befe9e0` passed Build GPTDeskTop #289, QA Release x64 #77, QA Hidden Chrome CDP #59, QA Crash Process Recovery #67, QA No-Response Watchdog #53, Development Delivery Receipts #167, Development Task Recovery #163 and Development Message Reload #35 before merge. The runtime suite passed all 141 tests after making the privacy-test fixture's rotation state explicit.
- **Post-1.8 Configuration Backup validation:** PR #36 head `e83e27dd94fe1f9f21bdf1258529f3bde6b2dd59` passed Build GPTDeskTop #293, QA Release x64 #81, QA Hidden Chrome CDP #63, QA Crash Process Recovery #71, QA No-Response Watchdog #57, Development Delivery Receipts #171, Development Task Recovery #167 and Development Message Reload #37 before merge. The runtime suite passed all 147 tests after making the Markdown documentation assertion formatting-safe; production backup code was unchanged by that correction.
- **Post-1.8 Configuration Restore validation:** PR #38 head `66d4f349ce58bdeec491e45405d3927c568cbc40` passed Build GPTDeskTop #298, QA Release x64 #86, QA Hidden Chrome CDP #68, QA Crash Process Recovery #76, QA No-Response Watchdog #62, Development Delivery Receipts #176, Development Task Recovery #172 and Development Message Reload #40 before merge. The runtime suite passed all 156 tests, including real-SQLite exact-URL merge, local identity/history preservation, new-monitor runtime reset, strict schema rejection and full-transaction rollback coverage.
- **Post-1.8 Conversation Identity validation:** PR #40 head `11c299f52785f5cea59e6ffd64e7f7063cf65ae6` passed Build GPTDeskTop #304, QA Release x64 #92, QA Hidden Chrome CDP #74, QA Crash Process Recovery #82, QA No-Response Watchdog #68, Development Delivery Receipts #182, Development Task Recovery #178 and Development Message Reload #44 before merge. The runtime suite passed all 176 tests after updating source-contract assertions for the intentional conversation terminology and shared identity validator; production identity-guard behavior was unchanged by those test-only corrections.

## Next Executable Task

There is no open implementation issue after #39. Continue post-1.8 maintenance by auditing the next concrete operator/runtime gap, creating a tracked issue, implementing it on a branch, and merging only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.

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

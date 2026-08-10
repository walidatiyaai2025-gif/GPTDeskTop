# TEST-001 — Thread-safe Monitor Activity Regression Collection

Issue: #145

Implementation branch: `agent/test-001-threadsafe-activity-collector`

Status: **DONE / VERIFIED / MERGED**

## Trigger

MON-013 documentation reconciliation Build GPTDeskTop #533 attempt 1 failed one runtime test while 340/341 tests passed:

`MonitorLiveTargetRevalidationTests.StartCommitsAndUsesFreshSameConversationTargetMetadata`

The same repository SHA passed the complete Build job on a single retry, and the MON-013 implementation had already passed the same test in PR and main validation.

## Root cause

The MON-013 regression tests subscribed to `ChatGptMonitorService.Activity` using a plain `List<string>`. `StartMonitorAsync` registers a background worker with `Task.Run`, and the worker can emit Activity concurrently with the caller-side `Started: ...` emission. Concurrent writes to `List<T>` are not safe and can lose or corrupt observations, making the assertion flaky even when production behavior is correct.

## Implementation

- use a shared `CaptureActivity` helper backed by `ConcurrentQueue<string>` for all Start-path Activity observations in `MonitorLiveTargetRevalidationTests`;
- preserve the existing `Started: Fresh live title` assertion unchanged;
- add a source-contract regression test that requires the thread-safe helper/queue and rejects the former unsafe `List<string>` / `activity.Add(message)` pattern;
- do not change production services, UI, SQLite, Chrome/CDP, monitoring, recovery, delivery, rotation, Setup, or release behavior.

## Delivery receipt

- Issue #145: **Closed / Completed**.
- PR #146: **Merged** — `test: make monitor Activity regression collectors thread-safe`.
- Final PR head: `05ed4734275d7c6dfd90859aa5772bfd0d9ed27b`.
- Main merge: `a06ed468c2a855ffd57df2863e459b41853583ee`.

## PR validation — 8/8 Green on the exact final head

- Build GPTDeskTop #535 — SUCCESS
- QA Passive Chat Wait #299 — SUCCESS
- QA Hidden Chrome CDP #305 — SUCCESS
- QA Crash Process Recovery #313 — SUCCESS
- QA Release x64 #323 — SUCCESS
- Development Delivery Receipts #413 — SUCCESS
- Development Task Recovery #409 — SUCCESS
- Development Message Reload #240 — SUCCESS

Build #535 passed the runtime automation suite containing the formerly flaky MON-013 test on its **first attempt**, then passed work-window lifecycle, runtime binding/integration, delivery invariants, multi-monitor delivery, saved-monitor rebinding, CDP reliability, crash recovery, application build, setup build, helper build, and rotation safety.

## Main validation — 8/8 Green on the exact merge

The main push for `a06ed468c2a855ffd57df2863e459b41853583ee` completed all eight required workflows successfully with zero failures. Key receipts include:

- Build GPTDeskTop #536 — SUCCESS
- QA Passive Chat Wait #300 — SUCCESS
- QA Crash Process Recovery #314 — SUCCESS
- QA Release x64 #324 — SUCCESS
- Development Task Recovery #410 — SUCCESS
- Development Message Reload #241 — SUCCESS
- QA Hidden Chrome CDP — SUCCESS
- Development Delivery Receipts — SUCCESS

Build #536 again passed the runtime automation suite on the first main attempt and completed every lifecycle/delivery/rebinding/CDP/crash/application/setup/helper/rotation gate successfully.

## Release impact

TEST-001 is test/documentation-only. It intentionally changes no application runtime behavior and does not require a new functional application artifact. Any automatic `Last release` publisher activity caused by normal main validation is release-pipeline bookkeeping, not a TEST-001 runtime feature change.

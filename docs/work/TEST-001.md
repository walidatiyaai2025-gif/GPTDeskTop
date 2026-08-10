# TEST-001 — Thread-safe Monitor Activity Regression Collection

Issue: #145

Branch: `agent/test-001-threadsafe-activity-collector`

Status: **IN PROGRESS**

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

## Validation plan

1. Runtime automation suite Green.
2. Full required PR workflow set Green on one exact head SHA.
3. Merge only after CI validation.
4. Reconcile this receipt with final PR/main merge information.

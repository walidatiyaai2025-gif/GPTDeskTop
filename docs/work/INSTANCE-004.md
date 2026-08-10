# INSTANCE-004 — Authoritative Instance-Takeover Resume Outcomes

Issue: #154

Implementation branch: `agent/instance-004-authoritative-resume-outcomes`

Status: **DONE / VERIFIED / MERGED / STABLE PUBLISHED**

## Objective

Make the monitor resume loop itself the single source of truth for every monitor that the outgoing process reported as running. Takeover accounting must not depend on a second Chrome/database pass after the resume attempt.

## Root cause

INSTANCE-003 added useful persisted requested/resumed/incomplete counts, but its post-resume reconciler could only infer the final state. `StartMonitorAsync` exceptions were already swallowed after logging, so they became generic `NotRunningAfterResume`. In addition, exhausted Chrome discovery threw before Program reached reconciliation, which meant a total Chrome-unavailable takeover could skip the new accounting persistence entirely.

## Implementation contract

- `ResumeRunningMonitorsAsync` returns `InstanceHandoffResumeReconciliation` directly;
- every distinct positive requested monitor ID receives exactly one outcome from the same loop that performs resume;
- static blockers are classified before Chrome dependency:
  - `MissingSavedMonitor`
  - `Disabled`
  - `InvalidConversationIdentity`
- existing Chrome discovery remains bounded to 20 attempts;
- exhausted discovery becomes `ChromeUnavailable` for otherwise resumable monitors instead of aborting accounting;
- unresolved stable live targets become `LiveTabUnresolved`;
- thrown monitor starts become `StartFailed` while exception details remain in diagnostics only;
- a completed start with no running worker becomes `NotRunningAfterStart`;
- successful starts are counted only when `IsMonitorRunning(id)` is true;
- Program persists the authoritative returned reconciliation directly and performs no second Chrome/database reconciliation pass;
- cancellation still propagates;
- no handoff protocol, SQLite schema, monitor identity/history, Chrome-preserving shutdown or stable-release semantics change.

## Files

- `src/GPTDeskTop/Services/InstanceHandoffCoordinator.cs`
- `src/GPTDeskTop/Services/InstanceHandoffResumeReconciler.cs`
- `src/GPTDeskTop/Program.cs`
- `tests/GPTDeskTop.RuntimeTests/InstanceHandoffResumeReconciliationTests.cs`
- `tests/GPTDeskTop.RuntimeTests/InstanceHandoffRegressionTests.cs`
- `docs/work/INSTANCE-004.md`

## Verification receipt

- Issue #154: **Completed**.
- Implementation PR #155: **Merged**.
- Final implementation head: `22eabe83cf66322e93e9a1139f23fef2b21fe090`.
- Main implementation merge: `d251f0c8d30fcf6845a4cba3ca85c0197f3557fd`.
- PR validation: **8/8 Green** on the final head:
  - Build GPTDeskTop #549
  - QA Passive Chat Wait #313
  - QA Hidden Chrome CDP #319
  - QA Crash Process Recovery #327
  - QA Release x64 #337
  - Development Delivery Receipts #427
  - Development Task Recovery #423
  - Development Message Reload #254
- Build #549 passed runtime automation, work-window lifecycle, runtime binding/integration, delivery invariants, multi-monitor delivery, saved-monitor rebinding, CDP reliability, crash recovery, application build, setup build, helper build and rotation safety.
- Main push validation: **8/8 Green** for exact source commit `d251f0c8d30fcf6845a4cba3ca85c0197f3557fd`; Build GPTDeskTop #550 passed the same full build/runtime gate set.
- Stable publisher: `Update Last release` #39 — **SUCCESS**.
- Stable publication commit: `8554f170688622c6096217c4266a623a96d6979d`.
- Stable build ID: `d251f0c8`.
- Stable version: `1.8.0.0`.
- Stable SHA-256: `14a44799239ab6936b4d63031a0efd64a8c6296e826ab7b907e36d5c4ba6e261`.

## CI repair note

The first PR Build #548 compiled successfully and ran 349 runtime tests. All five new INSTANCE-004 tests passed. The only failure was the older source-contract test `OnlyPreviouslyRunningEnabledMonitorsAreResumed`, which asserted the previous loop shape (`requestedIds.Contains(saved.Id)`). The production implementation was not changed for this failure; the test alone was updated to assert the new stronger authoritative-loop contract. The final head then passed all eight PR workflows.

## Stable release

`Last release/GPTDeskTop.exe` is published from source commit `d251f0c8d30fcf6845a4cba3ca85c0197f3557fd`, after all eight required main workflows passed for that same source commit. The release publisher records build ID `d251f0c8` and SHA-256 `14a44799239ab6936b4d63031a0efd64a8c6296e826ab7b907e36d5c4ba6e261`.

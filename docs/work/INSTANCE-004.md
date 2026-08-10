# INSTANCE-004 — Authoritative Instance-Takeover Resume Outcomes

Issue: #154

Implementation branch: `agent/instance-004-authoritative-resume-outcomes`

Status: **IN PROGRESS**

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
- `docs/work/INSTANCE-004.md`

## Validation plan

A Draft PR must pass the complete eight-workflow PR gate on one exact head before merge. After merge, the same implementation commit must pass the main push workflow set and the automated `Last release` publication must be verified before this receipt is marked completed.

# INSTANCE-003 — Reconcile Every Requested Monitor After Instance Takeover

Issue: #151

Implementation branch: `agent/instance-003-resume-reconciliation`

Status: **IN PROGRESS**

## Objective

Make instance takeover observable and accountable at the monitor level. A process-level takeover is not considered fully resumed merely because some monitor workers restarted; every distinct positive monitor ID that was running in the outgoing process must have a deterministic final outcome in the replacement process.

## Root cause

`InstanceHandoffCoordinator.ResumeRunningMonitorsAsync` intentionally tolerates per-monitor failures so one bad monitor does not crash the replacement process. Before INSTANCE-003, that tolerance had no reconciliation layer: missing/disabled/invalid/unresolved/start-failed monitors could be skipped and Program persisted only one aggregate resumed count.

## Implementation contract

- preserve the existing instance handoff protocol and existing `ResumeRunningMonitorsAsync` behavior;
- reconcile every distinct positive ID from `InstanceHandoffOffer.RunningMonitorIds` after the resume attempt;
- classify final outcomes as `Resumed` or bounded incomplete reasons:
  - `MissingSavedMonitor`
  - `Disabled`
  - `InvalidConversationIdentity`
  - `ChromeUnavailable`
  - `LiveTabUnresolved`
  - `NotRunningAfterResume`
- count a monitor as resumed only when `ChatGptMonitorService.IsMonitorRunning(id)` is true;
- persist the latest takeover requested/resumed/incomplete counts and incomplete IDs;
- emit one summarized diagnostic when any requested monitor remains incomplete;
- never crash the replacement process merely because resume is partial;
- preserve INSTANCE-002 single-instance ownership, MON-013 live-target validation, Chrome-preserving shutdown and SQLite schema.

## Files

- `src/GPTDeskTop/Services/InstanceHandoffResumeReconciler.cs`
- `src/GPTDeskTop/Program.cs`
- `tests/GPTDeskTop.RuntimeTests/InstanceHandoffResumeReconciliationTests.cs`
- `docs/work/INSTANCE-003.md`

## Validation plan

Full required GitHub Actions workflow set must be Green on one exact PR head before merge. After merge, main validation and stable publication must be verified before the work receipt is marked completed.

# INSTANCE-003 — Reconcile Every Requested Monitor After Instance Takeover

Issue: #151

Implementation branch: `agent/instance-003-resume-reconciliation`

Status: **DONE / VERIFIED / MERGED / STABLE PUBLISHED**

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

## Verification receipt

- Issue #151: **Closed / Completed**.
- PR #152: **Merged**.
- Final implementation head: `2afe79091ab144de0d6f1328c48a7a8db2952c62`.
- Main merge commit: `730ff3ecc11a06cf7ecb938371fc66ddddadfba4`.
- PR validation on the final head: **8/8 Green**.
  - Build GPTDeskTop #544 — SUCCESS
  - QA Passive Chat Wait #308 — SUCCESS
  - QA Hidden Chrome CDP #314 — SUCCESS
  - QA Crash Process Recovery #322 — SUCCESS
  - QA Release x64 #332 — SUCCESS
  - Development Delivery Receipts #422 — SUCCESS
  - Development Task Recovery #418 — SUCCESS
  - Development Message Reload #249 — SUCCESS
- Main push validation on `730ff3ecc11a06cf7ecb938371fc66ddddadfba4`: **8/8 Green**.
  - Build GPTDeskTop #545 — SUCCESS
  - QA Passive Chat Wait #309 — SUCCESS
  - QA Hidden Chrome CDP #315 — SUCCESS
  - QA Crash Process Recovery #323 — SUCCESS
  - all Release/Development push gates completed successfully on the same source commit.
- Build #545 passed runtime automation, work-window lifecycle, runtime binding/integration, delivery invariants, multi-monitor delivery, saved-monitor rebinding, CDP reliability, crash recovery, application build, setup build, helper build and rotation safety.

## CI repair note

The initial PR head exposed one compile-only namespace omission for `SavedMonitorTabResolver`. The repair added the missing `GPTDeskTop.Services.DevelopmentTaskEngine` import only; runtime semantics did not change. The repaired final head then passed the complete workflow set.

## Stable publication

`Last release/GPTDeskTop.exe` was published from the exact implementation source commit `730ff3ecc11a06cf7ecb938371fc66ddddadfba4` after **8/8 same-source validation**.

- Version: `1.8.0.0`
- Stable build ID: `730ff3ec`
- Informational version: `1.8.0+stable.730ff3ec.730ff3ecc11a06cf7ecb938371fc66ddddadfba4`
- SHA-256: `4b337b67589f94356b4febf71c074748474e3ab1d42d7a826719d9209b5e9009`
- Generated UTC: `2026-08-10T15:41:19Z`

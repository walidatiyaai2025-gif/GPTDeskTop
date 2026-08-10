# INSTANCE-002 — Preflight Replacement Before Committed Instance Shutdown

Issue: #148

Implementation branch: `agent/instance-002-preflight-commit`

Status: **IN PROGRESS**

## Objective

Harden INSTANCE-001 so the active GPTDeskTop runtime does not commit its browser-preserving shutdown merely because a replacement parsed the takeover payload. The replacement must first prove that it has opened the shared ownership primitive, validated the offered absolute runtime state/configuration, and is still alive at the commit boundary.

## Root cause

INSTANCE-001 used a two-phase offer/ACK protocol:

1. old runtime sends an absolute database/config/running-monitor offer;
2. new runtime ACKs the payload;
3. old runtime immediately starts committed shutdown;
4. only after ACK does the new runtime open/wait for the ownership mutex.

This preserves the no-dual-runtime invariant, but an invalid/dead replacement or local failure after ACK can cause the old runtime to exit before the new runtime is able to become primary. The existing orphan fallback is also unsafe for an ambiguous post-ACK transport failure because the replacement may no longer know whether the old runtime committed shutdown.

## Implementation contract

- open the shared takeover mutex before takeover negotiation declares readiness;
- validate the offered database path is absolute and currently exists;
- require effective `AppConfig`, valid Chrome debugging URL/port and positive core monitoring timings;
- verify the previous process is still alive before the replacement sends Ready;
- replace the old generic ACK with correlated `InstanceHandoffReady(RequestId, ProcessId)`;
- old runtime verifies Ready correlation and the replacement process liveness immediately before setting `_takeoverCommitted`;
- old runtime sends `InstanceHandoffCommit(Accepted=true)` before starting the committed shutdown callback;
- if commit-response write fails, reset `_takeoverCommitted` and keep the old runtime active;
- after Ready is sent, any missing/invalid/uncertain commit response fails closed and cannot enter orphan fallback;
- only after explicit commit acceptance does the replacement wait on the already-open ownership mutex;
- SQLite/runtime initialization remains after exclusive ownership acquisition in `Program.Main`;
- Chrome-preserving shutdown and previously-running monitor resume semantics remain unchanged.

## Residual crash boundary

This task removes avoidable pre-commit readiness/liveness failures. It does not claim to make arbitrary operating-system termination of the replacement process after a confirmed commit impossible. Eliminating that last catastrophic boundary would require a materially larger transferable/rollback ownership protocol and is intentionally outside INSTANCE-002 scope.

## Regression coverage

`InstanceHandoffRegressionTests.cs` now locks:

- ownership primitive opened before negotiation and ownership wait after commit acceptance;
- client ordering: Offer -> Preflight -> Ready -> CommitAccepted -> mutex wait;
- server ordering: Offer -> correlated Ready -> replacement liveness -> commit marker -> CommitAccepted -> shutdown;
- post-Ready transport ambiguity fails closed instead of using orphan fallback;
- old `InstanceHandoffAck` protocol is absent;
- existing absolute DB/config inheritance, browser-preserving shutdown, fatal-restart fallback and previously-running monitor resume contracts remain intact.

## Validation plan

Full required GitHub Actions workflow set must be Green on one exact PR head before merge, followed by same-SHA main validation and reconciliation.

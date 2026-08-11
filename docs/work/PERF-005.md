# PERF-005 — Harden CDP session retirement against concurrent socket disposal

Issue: #169 — **Closed / Completed**

Implementation branch: `agent/perf-005-cdp-retirement`

Implementation PR: #170

Baseline: `d9b687410f6c13b69254778e4d4311f990827b38`

Verified PR head: `c36718b8a22d41acabcf397bbdf0f754c01c86a6`

Squash merge to `main`: `07d916558f98f5db1deaba1554c4c98d9bce639b`

Status: **DONE / VERIFIED / MERGED**

## Problem

`DevToolsSession.Dispose()` previously called `ClientWebSocket.Dispose()` immediately after marking a session disposed. `Invalidate`, `Prune`, or `Clear` could therefore dispose the socket while a command still owned the per-session command gate and was inside `ConnectAsync`, `SendAsync`, or `ReceiveAsync`.

That race could surface as an `ObjectDisposedException` from the transport path instead of the existing transient CDP failure types. A prior Passive Chat Wait CI attempt had already exposed this symptom on the shared PERF-003 transport path.

A second interleaving also had to be closed: retirement could arrive after an active command had already checked the retirement flag in `finally` but just before it released the gate, leaving deferred cleanup unclaimed. PERF-005 uses post-release gate reacquisition so either the retiring caller or the exiting/waiting command will own cleanup without racing active I/O.

## Delivered contract

1. Session invalidation marks the session retired immediately so no later command can begin using it.
2. Retirement calls `ClientWebSocket.Abort()` as the interruption signal for any in-flight I/O.
3. Actual `ClientWebSocket.Dispose()` is performed only while the session command gate is exclusively owned.
4. Every command releases its active ownership first, then attempts retired-session cleanup by reacquiring the gate with a zero-time wait.
5. The retiring caller performs the same zero-time cleanup attempt. If an active or queued command owns the gate, that command will retry cleanup after it exits.
6. The guarded cleanup is idempotent through `_socketDisposed`, so the socket is physically disposed at most once.
7. A command already waiting on a retired session fails with the existing transient `IOException` invalidation path before touching the socket.
8. Any residual `ObjectDisposedException` raised by `ClientWebSocket` is normalized to `IOException`, which the monitor already classifies as transient.
9. `IsUsable` tolerates a disposed-socket observation and returns false instead of leaking `ObjectDisposedException` into pool selection.

## Compatibility boundary

- PERF-003 per-target session reuse is preserved.
- PERF-002 command timeout, caller cancellation, buffer pooling and payload bounds are preserved.
- PERF-004 streaming payload and SQLite polling optimizations are unchanged.
- Concurrent UI-POLISH-003 implementation and its verified-completion reconciliation were retained in the exact final validation base.
- Passive long-response wait, explicit-error recovery, auto reply, rotation, handoff, model routing and saved monitor identity behavior are unchanged.
- No UI, SQLite schema, release artifact or recovery-policy changes.

## Regression coverage

`ChromeDevToolsLongRunningStabilityRegressionTests` now additionally locks:

- retired/socket-disposed state tracking,
- post-command cleanup after gate release,
- zero-time exclusive gate reacquisition before physical socket disposal,
- a single guarded `ClientWebSocket.Dispose()` location,
- `ObjectDisposedException` normalization to transient `IOException`,
- and `IsUsable` tolerance of disposed socket state.

The first PR CI attempt compiled both the application and test project and passed 357/358 runtime tests. Its sole failure was the new source-structure assertion matching LF line endings literally on a Windows CRLF checkout. The assertion was replaced with ordered structural token checks before final validation; the final Build workflow then passed its Runtime automation tests and every subsequent gate.

## Verification receipts

All eight established GitHub Actions workflows passed on the exact final PR head `c36718b8a22d41acabcf397bbdf0f754c01c86a6`:

- Build GPTDeskTop #580 — Success
- QA Release x64 #368 — Success
- QA Hidden Chrome CDP #350 — Success
- QA Passive Chat Wait #344 — Success
- QA Crash Process Recovery #358 — Success
- Development Delivery Receipts #458 — Success
- Development Task Recovery #454 — Success
- Development Message Reload #285 — Success

PR #170 was squash-merged to `main` as `07d916558f98f5db1deaba1554c4c98d9bce639b`, and issue #169 closed as Completed.

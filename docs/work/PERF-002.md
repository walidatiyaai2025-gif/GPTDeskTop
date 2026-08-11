# PERF-002 — Harden long-running CDP polling against hangs and allocation churn

Issue: #157 — **Closed / Completed**

Implementation branch: `agent/perf-002-cdp-stability`

Implementation PR: #158

Verified PR head: `a958b25c4d21a582ffdaf161295396fb44586ea4`

Squash merge to `main`: `b6dbdc52fc8393f0908d8bb1636dd5a5794a1736`

Status: **DONE / VERIFIED / MERGED**

## Problem

The Chrome DevTools command path previously created a fresh 64 KB receive buffer for every command and used only the monitor lifetime cancellation token for `ConnectAsync`, `SendAsync` and `ReceiveAsync`.

For a monitor polling every second, repeated buffer allocation creates avoidable long-running GC pressure. More importantly, if Chrome accepts a DevTools WebSocket connection but never completes a command response, the monitor worker can remain blocked until the whole monitor is explicitly stopped.

## Delivered contract

1. Every CDP command now has an independent 12-second timeout.
2. Caller cancellation remains authoritative: an explicit monitor stop still propagates `OperationCanceledException`; only timeout-driven cancellation is converted to `TimeoutException`.
3. `TimeoutException` is already classified by the monitor as a transient Chrome/CDP failure, so the existing background retry/backoff path recovers without recording a false application crash.
4. The 64 KB receive buffer is rented from `ArrayPool<byte>` and always returned in `finally`, reducing per-poll allocation churn.
5. A single CDP message is capped at 2 MB to prevent unbounded `MemoryStream` growth if Chrome emits an unexpected payload.
6. Existing monitor identity, response detection, error-driven recovery, message rotation, SQLite, UI and single-instance behavior are unchanged.

## Regression coverage

`ChromeDevToolsLongRunningStabilityRegressionTests` locks:

- the independent CDP timeout,
- caller-cancellation preservation,
- receive-buffer pooling,
- message growth bounding,
- and `TimeoutException` remaining on the transient monitor retry path.

## Verification receipts

All eight established GitHub Actions workflows passed on the exact final PR head `a958b25c4d21a582ffdaf161295396fb44586ea4`:

- Build GPTDeskTop #553 — Success
- QA Release x64 #341 — Success
- QA Hidden Chrome CDP #323 — Success
- QA Passive Chat Wait #317 — Success
- QA Crash Process Recovery #331 — Success
- Development Delivery Receipts #431 — Success
- Development Task Recovery #427 — Success
- Development Message Reload #258 — Success

PR #158 was then squash-merged to `main` as `b6dbdc52fc8393f0908d8bb1636dd5a5794a1736`, and issue #157 closed as Completed.

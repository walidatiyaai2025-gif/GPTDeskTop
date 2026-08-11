# PERF-003 — Reuse CDP WebSocket sessions for long-running monitors

Issue: #161

Implementation branch: `agent/perf-003-cdp-session-reuse`

Status: **IMPLEMENTED / VALIDATION PENDING**

## Problem

PERF-002 prevents one Chrome DevTools command from hanging forever and pools its receive buffer, but every poll still created and destroyed a complete `ClientWebSocket`. At one-second monitor intervals that means thousands of WebSocket/TCP connection lifecycles during a long session, multiplied by the number of monitored chats.

## Delivered contract

1. Chrome DevTools transport is owned by `ChromeDevToolsSessionPool`.
2. One reusable `ClientWebSocket` session is retained per live Chrome target ID and WebSocket debugger URL.
3. Commands for the same target are serialized through a per-session `SemaphoreSlim`, keeping CDP request/response framing deterministic.
4. Command IDs increment within a session; unrelated CDP event messages are drained until the matching command response arrives.
5. The PERF-002 12-second command timeout, caller cancellation semantics, pooled 64 KB receive buffer, and 2 MB message safety bound are preserved.
6. Timeout, caller cancellation, WebSocket failure, invalid JSON, connection close, and I/O failure mark only that target session broken. The next command creates a fresh session automatically.
7. `GetTabsAsync` prunes sessions whose Chrome targets no longer exist.
8. `CloseTabAsync` invalidates the closed target session, while `CloseAllMonitorTabsAsync` clears all sessions during normal application shutdown.
9. Existing ChatGPT response detection, passive long-response wait, auto reply, recovery, rotation, saved monitor identity, SQLite and UI semantics are unchanged.

## Regression coverage

`ChromeDevToolsLongRunningStabilityRegressionTests` now locks:

- bounded command timeout and caller cancellation,
- pooled receive buffers and bounded payload growth,
- per-target session reuse,
- serialized command access,
- broken-session recreation,
- target pruning and close invalidation,
- and timeout classification as a recoverable monitor failure.

## Validation

GitHub Actions validation is required on the implementation PR before merge.

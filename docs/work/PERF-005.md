# PERF-005 — Harden CDP session retirement against concurrent socket disposal

Status: **PLANNED**

## Goal
Prevent a retired Chrome DevTools session from disposing its `ClientWebSocket` while a command still owns the per-session command gate.

## Compatibility boundary
- Preserve passive long-response waiting.
- Preserve CDP command timeout and caller cancellation semantics.
- Preserve per-target session reuse from PERF-003.
- Preserve PERF-004 hot-loop behavior.
- No UI, SQLite schema, monitor identity, release artifact, or recovery-policy changes.

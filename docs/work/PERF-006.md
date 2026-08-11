# PERF-006 — Cache ChatGPT DOM state with an event-driven page observer

Issue: #173

Branch: `agent/perf-006-chat-state-cache`

Baseline main: `b39c1f99056f884665c8821a125c8212a01c80f6`

Status: **IMPLEMENTED / VALIDATION PENDING**

## Problem

The monitor polled `GetChatStateAsync` as frequently as once per second. Before PERF-006 every poll sent the full chat-state JavaScript program through CDP and rescanned the relevant ChatGPT DOM even when the page had not changed.

PERF-004 already prevented serialization of the growing assistant response while generation was active, but the repeated JavaScript parsing and DOM queries remained a steady-state CPU/allocation cost.

## Delivered contract

1. `GetChatStateAsync` first executes a tiny steady-state expression that reads a versioned page-side cache.
2. If the helper is absent, such as after navigation or reload, the service installs it automatically and returns the first snapshot.
3. A `MutationObserver` marks the snapshot dirty; it does not rescan or parse the page in the mutation callback.
4. The next monitor poll recomputes only when dirty. Unchanged pages return the cached snapshot directly.
5. The helper keeps the existing assistant count, generation detection, explicit error detection, and last stable assistant text semantics.
6. While the assistant is generating, the growing assistant body is still not serialized into the CDP response.
7. DOM scans use direct loops instead of spread-array materialization for stop, streaming, and error candidates.
8. A version mismatch disconnects the previous helper observer before installing the new version.

## Compatibility boundary

- Passive long-response wait is unchanged: elapsed time alone never reloads a healthy chat.
- PERF-002 command timeout/buffer bounds are preserved.
- PERF-003 persistent per-target CDP sessions are preserved.
- PERF-004 hot-loop payload and settings refresh behavior are preserved.
- PERF-005 session retirement safety is preserved.
- Rotation, explicit-error recovery, auto reply, handoff, model routing, and saved monitor identity are unchanged.
- No SQLite schema, UI, release artifact, or recovery-policy changes.

## Regression coverage

`MonitorHotLoopPerformanceRegressionTests` now locks:

- no growing assistant body serialization while generating,
- versioned page-side state cache,
- `MutationObserver` dirty marking,
- cached return when no DOM mutation occurred,
- tiny steady-state CDP read expression,
- automatic helper reinstall after page globals disappear.

## Validation

All established PR GitHub Actions workflows must pass on the exact final PR head before merge.

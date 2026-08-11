# PERF-008 — Share runtime settings refresh across monitor workers

Issue: #181

Branch: `agent/perf-008-shared-runtime-settings`

Status: **IMPLEMENTED / DEPENDS ON PERF-007 MERGE**

## Problem

PERF-004 reduced rotation-setting reads from every poll to every five seconds, but each monitor worker still performed its own pair of SQLite reads. With multiple monitors, workers could reach the same refresh boundary and create a small settings-read stampede.

## Delivered

- Added one immutable `RuntimeSettingsSnapshot` shared by the `ChatGptMonitorService` instance.
- Added `_runtimeSettingsRefreshGate` so only one worker refreshes an expired snapshot.
- Fast-path reads use `Volatile.Read` and do not take the gate while the snapshot is valid.
- The two independent SQLite reads start together and are awaited with `Task.WhenAll`.
- The refreshed snapshot carries its own `ExpiresUtc`, preserving the five-second maximum staleness contract.
- Workers that arrive concurrently after expiry wait for the first refresher and then reuse its snapshot.
- Initial monitor startup also uses the shared snapshot, so starting several monitors together no longer repeats identical setting reads.

## Compatibility

- Dynamic settings still propagate within at most five seconds.
- Passive long-response waiting is unchanged.
- Rotation threshold and rotation start-message semantics are unchanged.
- No SQLite schema, Chrome/CDP, recovery, monitor identity, UI, or release changes.

## Regression coverage

`MonitorHotLoopPerformanceRegressionTests` now locks the shared gate, volatile snapshot fast path, concurrent refresh, five-second expiry, and exactly one source read for each runtime setting.

## Validation

Rebase onto the verified PERF-007 main head, then require all established PR workflows to pass on the exact final head before merge.

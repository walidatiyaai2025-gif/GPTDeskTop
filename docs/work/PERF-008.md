# PERF-008 — Share runtime settings refresh across monitor workers

Issue: #181 — **Closed / Completed**

Branch: `agent/perf-008-shared-runtime-settings`

PR: #183

Verified PR head: `9af9e0eaa45c5e9c2467323fbdfa850c87dce35f`

Squash merge to `main`: `35ccea11a4d2e43f529bbe712318578ded256c83`

Status: **DONE / VERIFIED / MERGED**

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

`MonitorHotLoopPerformanceRegressionTests` locks the shared gate, volatile snapshot fast path, concurrent refresh, five-second expiry, and exactly one source key for each runtime setting. The first validation attempt exposed a formatting-sensitive source assertion; the assertion was corrected to count the setting key literal rather than method-call whitespace, and the final runtime suite passed.

## Verification receipts

All eight established GitHub Actions workflows passed on exact final head `9af9e0eaa45c5e9c2467323fbdfa850c87dce35f`:

- Build GPTDeskTop #601 — Success
- QA Release x64 #389 — Success
- QA Hidden Chrome CDP #371 — Success
- QA Passive Chat Wait #365 — Success
- QA Crash Process Recovery #379 — Success
- Development Delivery Receipts #479 — Success
- Development Task Recovery #475 — Success
- Development Message Reload #306 — Success

PR #183 was squash-merged to `main` as `35ccea11a4d2e43f529bbe712318578ded256c83`.

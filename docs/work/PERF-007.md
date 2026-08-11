# PERF-007 — Index and bound MessageLogs history growth

Issue: #178 — **Closed / Completed**

Branch: `agent/perf-007-log-retention`

PR: #180

Verified head: `94e7f33b2f1cf3a175add4522d94bf27e3ef9698`

Squash merge: `07824d01fa75d4d0c522f73df22540c76861d1fc`

Status: **DONE / VERIFIED / MERGED**

## Delivered

- Added `IX_MessageLogs_MonitorId_Id` for monitor-scoped newest-first history reads.
- Added `IX_MessageLogs_Timestamp_Id` for age-retention scans.
- Added defaults: `MessageLogRetentionDays=90`, `MessageLogMaxRows=50000`, `MessageLogCleanupEveryRows=250`.
- Added a throttled SQLite `AFTER INSERT` retention trigger covering all MessageLogs writers.
- Days/max-row retention can be disabled independently with `0`.
- Cleanup cadence is clamped to 10–10000 rows; enabled row cap to 100–500000; enabled age retention to 1–3650 days.
- Startup applies the performance migration after the base LocalDatabase schema migration.

## Regression coverage

`MessageLogRetentionPerformanceTests` creates a real temporary SQLite database, verifies the planner uses `IX_MessageLogs_MonitorId_Id`, inserts 120 rows with a 100-row cap / 10-row cleanup cadence, verifies exactly 100 rows remain, and verifies all retention settings are wired into the trigger.

## Compatibility

No Chrome/CDP, passive wait, response detection, rotation, recovery, monitor identity, model routing, UI, or release behavior changes.

## Verification receipts

All eight established PR workflows passed on exact head `94e7f33b2f1cf3a175add4522d94bf27e3ef9698`:

- Build GPTDeskTop #596 — Success
- QA Release x64 #384 — Success
- QA Hidden Chrome CDP #366 — Success
- QA Passive Chat Wait #360 — Success
- QA Crash Process Recovery #374 — Success
- Development Delivery Receipts #474 — Success
- Development Task Recovery #470 — Success
- Development Message Reload #301 — Success

PR #180 was squash-merged to `main` as `07824d01fa75d4d0c522f73df22540c76861d1fc`.

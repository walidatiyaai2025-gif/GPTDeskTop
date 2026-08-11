# PERF-007 — Index and bound MessageLogs history growth

Issue: #178

Branch: `agent/perf-007-log-retention`

Baseline: `5d69eb53636cccfcb4626149f293a7b67e566f94`

Status: **IMPLEMENTED / VALIDATION PENDING**

## Delivered

- Added `IX_MessageLogs_MonitorId_Id` for monitor-scoped newest-first history reads.
- Added `IX_MessageLogs_Timestamp_Id` for age-retention scans.
- Added default settings:
  - `MessageLogRetentionDays=90`
  - `MessageLogMaxRows=50000`
  - `MessageLogCleanupEveryRows=250`
- Added a throttled SQLite `AFTER INSERT` retention trigger.
- Retention can be disabled independently with `0` for days or max rows.
- Cleanup cadence is clamped to 10–10000 inserted rows.
- Row cap is clamped to 100–500000 when enabled.
- Age retention is clamped to 1–3650 days when enabled.
- Startup applies the performance migration immediately after the base `LocalDatabase.InitializeAsync` schema migration.

## Why a trigger

Several application flows insert MessageLogs directly inside larger transactions. A database trigger applies the retention contract consistently to all writers without adding a second cleanup write to every `AddLogAsync` call. Cleanup runs only on the configured cadence, avoiding steady write amplification.

## Regression coverage

`MessageLogRetentionPerformanceTests` creates a real temporary SQLite database, applies the migration, verifies the query planner chooses `IX_MessageLogs_MonitorId_Id`, inserts 120 rows with a 100-row cap / 10-row cleanup cadence, verifies exactly 100 rows remain, and confirms all three retention settings are wired into the trigger.

## Compatibility

No Chrome/CDP, passive wait, response detection, rotation, recovery, monitor identity, model routing, UI, or release behavior changes.

## Validation

Merge only after all established PR workflows pass on the exact final head.

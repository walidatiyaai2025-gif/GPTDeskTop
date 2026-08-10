# HEALTH-001 — Runtime Health Auto-Refresh After Runtime State Changes

Issue: #137 — **IN PROGRESS**

Implementation branch: `agent/health-001-runtime-health-refresh`

Status: **IMPLEMENTED / CI PENDING**

## Objective

Prevent the Runtime Health banner from remaining on an obsolete `DEGRADED` Chrome/CDP snapshot after monitor Chrome becomes reachable and monitor workers successfully start or stop.

## Observed production symptom

A stable 1.8.0 runtime screenshot captured on 2026-08-10 showed Runtime Health still displaying `DEGRADED` with `SQLite is reachable, but Chrome/CDP is unavailable` and an old check timestamp, while Live Activity subsequently showed monitor Chrome launching and multiple saved monitors starting successfully. The Saved Monitors grid showed active workers, but the overall health banner still represented the earlier failed probe.

## Root cause

`RuntimeHealthControl` subscribes to `ChatGptMonitorService.RunningStateChanged`, but the existing handler called only `UpdateRunningMonitorMetric`. That repainted the Saved / Running counter without re-running the Chrome/CDP and SQLite probes or re-rendering the overall `RuntimeHealthSnapshot`.

Therefore a failed startup probe could remain visible indefinitely until the operator manually pressed Refresh, even after runtime recovery was proven by successful monitor starts.

## Delivered implementation

1. Added a coalesced `RequestRefresh` path inside `RuntimeHealthControl`.
2. `RunningStateChanged` still updates the fast Saved / Running metric immediately, then requests a complete health refresh.
3. If a health probe is already running, the request is remembered through `_refreshRequested` instead of starting a second overlapping probe.
4. When the current probe completes, one queued refresh is launched so Start All / Stop All bursts cannot leave the final health snapshot stale.
5. The existing `_loading` single-flight guard and five-second health-probe timeout remain unchanged.
6. No timer/background polling loop was added; refreshes remain event-driven or operator-triggered.
7. Health probing remains read-only. No monitor Start/Stop, SQLite configuration mutation, ChatGPT DOM action, recovery action or release behavior was added to the health refresh path.

## Regression coverage

`RuntimeHealthAutoRefreshRegressionTests.cs` locks the new contract:

- monitor running-state changes must request a full health probe in addition to repainting the running metric;
- refresh requests received while a probe is active are coalesced and replayed after completion;
- the existing single-flight `_loading` guard remains present;
- the change does not introduce a WinForms timer or monitor mutation calls.

Existing `RuntimeHealthUiRegressionTests.cs` continues to cover bounded five-second probes, read-only behavior, stable-conversation counting, recovery blockers, explicit recovery retry safety and subscription disposal.

## Compatibility

No ChatGPT DOM/CDP selectors, passive long-response waiting, message delivery, conversation rotation, crash recovery algorithm, monitor lifecycle semantics, SQLite schema, Setup packaging or stable-release publisher behavior is intentionally changed.

## Validation

Implementation source commit: `95c588ce78ce84a5e5b6d70c09dbc3aa20833384`.

Regression test commit: `57d217dde34e517fc40ac282ac118b6b4d7e8d72`.

Pre-PR compare against base `b59f44de0e53c9f5a2fb6bec5c41be1c9e98ed9f` confirms only the intended Runtime Health source plus the new regression test are changed before this work receipt was added.

Full GitHub Actions validation is pending the implementation PR and must be recorded here before merge.
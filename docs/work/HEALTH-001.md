# HEALTH-001 — Runtime Health Auto-Refresh After Runtime State Changes

Issue: #137 — **COMPLETED**

Implementation branch: `agent/health-001-runtime-health-refresh`

Implementation PR: #138

Status: **DONE / VERIFIED / MERGED / STABLE PUBLISHED**

## Objective

Prevent the Runtime Health banner from remaining on an obsolete `DEGRADED` Chrome/CDP snapshot after monitor Chrome becomes reachable and monitor workers successfully start or stop.

## Observed production symptom

A stable 1.8.0 runtime screenshot captured on 2026-08-10 showed Runtime Health still displaying `DEGRADED` with `SQLite is reachable, but Chrome/CDP is unavailable` and an old check timestamp, while Live Activity subsequently showed monitor Chrome launching and multiple saved monitors starting successfully. The Saved Monitors grid showed active workers, but the overall health banner still represented the earlier failed probe.

## Root cause

`RuntimeHealthControl` subscribes to `ChatGptMonitorService.RunningStateChanged`, but the previous handler called only `UpdateRunningMonitorMetric`. That repainted the Saved / Running counter without re-running the Chrome/CDP and SQLite probes or re-rendering the overall `RuntimeHealthSnapshot`.

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

## Verified receipts

- Issue #137: closed as Completed.
- Implementation PR #138: squash-merged.
- Verified implementation head: `b47c345829e6798b89269ed0cc4b0543ba4c720e`.
- Main implementation merge: `8e9a8fc85a7913af0f46fa1fc12cd2ed18260bca`.

### PR-head validation

All eight required PR-triggered workflows passed on the exact final implementation head:

- Build GPTDeskTop #513
- QA Release x64 #301
- QA Passive Chat Wait #277
- QA Hidden Chrome CDP #283
- QA Crash Process Recovery #291
- Development Delivery Receipts #391
- Development Task Recovery #387
- Development Message Reload #218

### Main validation

All eight required push-triggered workflows completed successfully on the exact implementation merge `8e9a8fc85a7913af0f46fa1fc12cd2ed18260bca`; no failed or cancelled run was observed. Build GPTDeskTop #515, QA Passive Chat Wait #279 and Development Task Recovery #389 are among the exact same-SHA main receipts.

### Stable publication

`Update Last release` run #25 (`31382034432`) completed successfully from the verified implementation merge. The stable publisher advanced `Last release` with:

- Version: `1.8.0.0`
- Stable build ID: `8e9a8fc8`
- Source commit: `8e9a8fc85a7913af0f46fa1fc12cd2ed18260bca`
- Informational version: `1.8.0+stable.8e9a8fc8.8e9a8fc85a7913af0f46fa1fc12cd2ed18260bca`
- Generated UTC: `2026-08-10T11:07:28Z`
- Validation: 8/8 required GitHub Actions workflows passed for the same source commit
- SHA-256: `4a0c69748c75a967789def3ad603a25f2b02513bd8002b6ee79f0a17b5463f78`
- Automated release commit on main: `67631454600895894bd9439aa8191274315a11c5`

## Compatibility

No ChatGPT DOM/CDP selectors, passive long-response waiting, message delivery, conversation rotation, crash recovery algorithm, monitor lifecycle semantics, SQLite schema, Setup packaging or stable-release publisher behavior was intentionally changed by HEALTH-001.
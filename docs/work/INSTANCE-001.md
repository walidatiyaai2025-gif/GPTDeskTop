# INSTANCE-001 — Seamless Single-Instance Runtime Takeover

Issue: #141 — **COMPLETED**

Implementation branch: `agent/instance-001-seamless-handoff`

Implementation PR: #142

Status: **DONE / VERIFIED / MERGED / STABLE PUBLISHED**

## Objective

When a second GPTDeskTop executable is launched while an older copy is already running, hand runtime ownership to the new process without allowing two active monitor runtimes and without closing the dedicated monitor Chrome tabs or cancelling a ChatGPT response already generating in the browser.

## Existing gap

Before INSTANCE-001 there was no application-level single-instance ownership or takeover protocol. Two copies could therefore start independently against the same logical monitor workload. In addition, SQLite was resolved relative to each executable directory, so launching a newer EXE from a different folder could silently create/use a different `appdata.db` instead of inheriting the running instance's configuration.

Normal MainForm shutdown also intentionally stops monitor workers and closes monitor Chrome tabs. Reusing that normal close path for an upgrade/takeover would interrupt the browser conversation and could cancel the continuity expected from a hot replacement.

## Delivered design

### Exclusive ownership

- A named Windows mutex (`Local\\GPTDeskTop.SingleInstance.v1`) establishes exactly one active application runtime.
- Process-probe command modes continue to bypass the application single-instance path so existing crash/CDP/watchdog automation remains isolated.
- A second normal application instance does not initialize SQLite or monitor workers until it has either become the first owner or completed a safe takeover and acquired the mutex.
- If takeover cannot be negotiated or the previous owner does not release within the bounded timeout, the second runtime fails closed rather than running concurrently.
- The existing fatal-restart path is preserved with a bounded orphaned-owner fallback: a replacement may claim the mutex only after the previous owner has released it or Windows reports it abandoned.

### Two-phase local IPC takeover

- The active instance exposes a local named-pipe handoff endpoint.
- The new process sends a takeover request.
- Before creating the offer, the old process persists the live MainForm operator layout on the UI thread, including the current window bounds/state and workspace splitter ratios.
- The old process captures an offer containing:
  - the absolute active SQLite database path;
  - the effective `AppConfig` (Chrome + monitoring + database configuration);
  - the exact saved Monitor IDs currently running;
  - previous process identity / request correlation data.
- The new process must successfully parse the offer and send an ACK before the old process commits shutdown.
- Only after ACK does the old process stop its monitor workers and exit.
- The new process waits for the old process to release the named mutex before any monitor workers are resumed, preventing old/new reply overlap.

### Preserving settings across executable folders

- The handoff offer carries the old process's absolute database path.
- The new process uses that exact path even if its EXE resides in another folder/version directory.
- The new process also uses the old process's effective `AppConfig`, so Chrome CDP and monitoring configuration remain consistent during the replacement.
- Saved monitors, auto reply, timers, delays, rotation/model settings, notification settings, recovery receipts, history, runtime UI expansion state and other SQLite-backed settings remain authoritative in the same database rather than being copied into a second database.
- The live MainForm window/splitter layout is explicitly flushed immediately before the handoff snapshot so recent operator layout changes are not lost merely because the takeover bypasses normal form closing.

### Keeping browser work alive

The committed takeover path is intentionally different from normal operator shutdown:

1. persist the current operator workspace and capture running Monitor IDs;
2. send and ACK the takeover payload;
3. stop only the old process's monitor workers;
4. mark shutdown clean and dispose the development runtime within bounded timeouts;
5. exit the old application process **without calling MainForm normal close cleanup**;
6. therefore do **not** call `CloseAllMonitorTabsAsync` during takeover;
7. the dedicated Chrome process and its open ChatGPT conversations remain alive;
8. if ChatGPT is still generating a response, generation continues in Chrome while process ownership changes;
9. the new process reattaches to the existing stable conversation tabs and restarts only the previously-running enabled monitor IDs.

Normal user exit remains unchanged and still performs the existing StopAll + monitor-Chrome-tab cleanup.

## Recovery interaction

The new owner opens the inherited SQLite database, runs the existing crash-recovery startup contract, and then resumes the takeover Monitor IDs. The old owner marks its committed handoff shutdown clean before process exit. Existing delivery receipts, passive long-response behavior, conversation identity checks and recovery state remain authoritative.

## Failure policy

- No `Process.Kill` / `CloseMainWindow` takeover fallback is used.
- Failure to receive a valid offer or ACK safely results in no second active runtime unless the old owner has genuinely disappeared and the named mutex can be exclusively acquired.
- Failure to acquire ownership after ACK prevents the new runtime from starting.
- Monitor Chrome remains separate from the application process and is intentionally not torn down by committed takeover.

## Regression coverage

`InstanceHandoffRegressionTests.cs` locks:

- ownership acquisition before database/runtime initialization;
- absolute database path + effective config inheritance;
- current operator workspace persistence before handoff snapshot creation;
- worker stop without Chrome tab teardown;
- two-phase offer/ACK protocol;
- mutex release before new worker resume;
- bounded orphaned-owner handling for fatal-restart races;
- resume restricted to previously-running enabled Monitor IDs;
- fail-closed behavior and absence of forced-kill takeover code.

## Compatibility

No ChatGPT DOM selector, delivery algorithm, passive long-response timeout behavior, conversation rotation algorithm, SavedMonitor schema, Setup packaging or stable-release publisher contract is intentionally changed by INSTANCE-001.

## Verified receipts

- Issue #141: closed as Completed.
- Implementation PR #142: squash-merged.
- Verified implementation head: `6451cfb70ccd17343b1a0d4e6ba247258a413687`.
- Main implementation merge/source: `4a6e9cc0435b8398a8d780525a7b9323eacdf5b6`.

### PR-head validation

The first CI pass correctly rejected an intermediate head because the new coordinator referenced `SavedMonitorTabResolver` without its `DevelopmentTaskEngine` namespace. The namespace defect was fixed before merge.

All eight required PR-triggered workflows then passed on the final head `6451cfb70ccd17343b1a0d4e6ba247258a413687`:

- Build GPTDeskTop #525
- QA Release x64 #313
- QA Passive Chat Wait #289
- QA Hidden Chrome CDP #295
- QA Crash Process Recovery #303
- Development Delivery Receipts #403
- Development Task Recovery #399
- Development Message Reload #230

### Main validation and stable publication

The stable publisher accepted the exact merged source after the required main workflows completed successfully.

- Update Last release run: #29 — **SUCCESS**
- Stable build ID: `4a6e9cc0`
- Source commit: `4a6e9cc0435b8398a8d780525a7b9323eacdf5b6`
- Informational version: `1.8.0+stable.4a6e9cc0.4a6e9cc0435b8398a8d780525a7b9323eacdf5b6`
- Generated UTC: `2026-08-10T11:47:20Z`
- Validation: 8/8 required GitHub Actions workflows passed for the same source commit
- SHA-256: `d9a5f6f6be04dc50675b10b2b21fcac7fc3367891d9a952d3abe96810eef515d`

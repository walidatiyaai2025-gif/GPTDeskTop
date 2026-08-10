# INSTANCE-001 — Seamless Single-Instance Runtime Takeover

Issue: #141 — **IN PROGRESS**

Implementation branch: `agent/instance-001-seamless-handoff`

Status: **IMPLEMENTED / CI PENDING**

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

### Two-phase local IPC takeover

- The active instance exposes a local named-pipe handoff endpoint.
- The new process sends a takeover request.
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
- Saved monitors, auto reply, timers, delays, rotation/model settings, notification settings, recovery receipts, history and other SQLite-backed settings remain authoritative in the same database rather than being copied into a second database.

### Keeping browser work alive

The committed takeover path is intentionally different from normal operator shutdown:

1. capture running Monitor IDs;
2. ACK the takeover payload;
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
- Failure to receive a valid offer or ACK safely results in no second active runtime.
- Failure to acquire ownership after ACK also prevents the new runtime from starting.
- Monitor Chrome remains separate from the application process and is intentionally not torn down by committed takeover.

## Regression coverage

`InstanceHandoffRegressionTests.cs` locks:

- ownership acquisition before database/runtime initialization;
- absolute database path + effective config inheritance;
- worker stop without Chrome tab teardown;
- two-phase offer/ACK protocol;
- mutex release before new worker resume;
- resume restricted to previously-running enabled Monitor IDs;
- fail-closed behavior and absence of forced-kill takeover code.

## Compatibility

No ChatGPT DOM selector, delivery algorithm, passive long-response timeout behavior, conversation rotation algorithm, SavedMonitor schema, Setup packaging or stable-release publisher contract is intentionally changed by INSTANCE-001.

## Validation

Implementation commits are on `agent/instance-001-seamless-handoff`.

Full GitHub Actions validation is required on the final PR head before merge. After merge, exact-SHA main validation and stable `Last release` publication must be recorded before this task is reconciled as DONE / VERIFIED / STABLE PUBLISHED.

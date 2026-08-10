# PERF-001 — Development Plan timer demand scheduling

Issue: #118

Implementation branch: `agent/batch-ui-resource-lifecycle-5`

Status: **DONE / VERIFIED / MERGED**

Implementation PR: #124

Verified implementation head: `f40151fb76e68b644219a0854a9c624b1f5189c8`

Merged `main` commit: `0a85e62e9b811aabc0bdeb3a88793a17793d8b0a`

Issue state: **Completed**

## Objective

Stop the Development Plan dashboard from waking the WinForms UI every 500 ms when no visible Working/Cooling countdown needs repainting.

## Delivered contract

1. The timer is not started unconditionally by the constructor.
2. It runs only while the control is visible, expanded and the engine is Working or Cooling.
3. It stops when hidden, collapsed, Stopped, Paused or Completed.
4. Engine events and user actions still render immediately.
5. Expanding or showing the dashboard recalculates the countdown immediately and resumes the timer only when needed.
6. Existing lifecycle buttons, scheduling and message-delivery behavior remain unchanged.

## Verification receipt

- Final PR head passed the complete current 9/9 repository workflow set.
- Merge commit `0a85e62e9b811aabc0bdeb3a88793a17793d8b0a` passed the same 9/9 main validation set.
- `Update Last release` run #19 completed successfully for the same merge source.
- Automated release commit: `062ee87e7f37cb7e9d62913c59a8def0e99e3aba`.
- Stable build ID: `0a85e62e`.
- Published EXE size: 72,793,785 bytes.
- Published EXE SHA-256: `c535ca35075868f0d0b1567fb7e0127d998c8e299fd87f5e3d7b7b36d410460b`.

## Compatibility

No lifecycle-button, scheduling, message-delivery, monitoring, recovery, SQLite, Setup or release-publisher behavior changed.

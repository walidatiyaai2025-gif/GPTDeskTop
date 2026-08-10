# PERF-001 — Development Plan timer demand scheduling

Issue: #118

Implementation branch: `agent/batch-ui-resource-lifecycle-5`

Status: **IN PROGRESS**

## Objective

Stop the Development Plan dashboard from waking the WinForms UI every 500 ms when no visible Working/Cooling countdown needs repainting.

## Implementation contract

1. The timer is not started unconditionally by the constructor.
2. It runs only while the control is visible, expanded and the engine is Working or Cooling.
3. It stops when hidden, collapsed, Stopped, Paused or Completed.
4. Engine events and user actions still render immediately.
5. Expanding or showing the dashboard recalculates the countdown immediately and resumes the timer only when needed.
6. Existing lifecycle buttons, scheduling and message-delivery behavior remain unchanged.

## Validation required before merge

The final batch PR head must pass all eight established repository workflows.

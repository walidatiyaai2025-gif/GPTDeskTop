# GPTDeskTop Development Plan

## Phase 1 — Persistent Development Task Runner

### DEV-001 — Editable message catalog
- Source: `src/GPTDeskTop/task-messages.json`
- Keep development-plan prompts editable without changing C# code.
- Messages are consumed sequentially and wrap safely after the final message.

### DEV-002 — Work/Cooling scheduler
- Default work window: 10 minutes.
- Default cooling window: 5 minutes.
- Cooling is cooperative rate limiting; it is not intended to bypass quotas or access controls.
- Scheduler persists its phase and last cycle state in SQLite.

### DEV-003 — Persistent checkpoints
For each monitor the runner stores:
- last status
- last message
- timestamp

The global runner stores:
- next message index
- current phase
- last cycle timestamp
- last cycle send count

### DEV-004 — Startup resume
On application startup the runner restores the persisted message index and checkpoint state, then resumes the next development-plan message for enabled monitors whose saved ChatGPT tab is still available.

### DEV-005 — Safe delivery
- Do not send while ChatGPT is generating.
- Verify the user message was accepted before recording it as sent.
- Record success/failure in Stored History.
- Keep the same monitor ID and saved tab association.

## Phase 2 — UI controls

- Add a Development Automation settings panel.
- Enable/disable automation.
- Work window minutes.
- Cooling window minutes.
- Resume on startup.
- Open the message catalog from the UI.
- Show current phase, next message index, last checkpoint and last cycle result.
- Add Start Now / Pause / Stop controls.

## Phase 3 — Advanced execution

- Associate an explicit development plan/project with each monitor.
- Add task IDs and completion states.
- Require a checkpoint after every completed task.
- Detect a completed task response before advancing the task queue.
- Support multiple plans without mixing their checkpoints.
- Add retry/backoff for transient browser failures.

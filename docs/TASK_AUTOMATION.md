# Development Task Automation

## Purpose

GPTDeskTop can execute the development-plan message queue in bounded work windows with a persisted checkpoint. The scheduler is a cooperative rate limiter and is not intended to bypass service quotas, access controls, or platform protections.

## Defaults

- Work window: 10 minutes
- Cooling window: 5 minutes
- Message catalog: `task-messages.json`
- Resume on startup: enabled
- One logical development-plan message per work cycle
- Message sequence: 10 editable messages, repeated cyclically

## Message editing

Edit `task-messages.json` to add, remove, or reorder development-plan messages. Do not edit the C# service for normal message changes.

Supported placeholders:

- `{planId}`
- `{planTitle}`
- `{step}`
- `{total}`

## Persistence

The automation stores phase, message index, per-monitor checkpoints, timestamps, and the last delivery status in SQLite settings. This lets startup recovery continue from the next message rather than resetting the sequence.

## Startup behavior

When `ResumeOnStartup` is enabled, the service loads the persisted message index and resumes the development-plan queue. Saved monitors are resolved again by their persisted Monitor ID and current Chrome Tab ID before a message is delivered.

## Cooling behavior

At the end of a work window the service enters `Cooling`, waits for the configured cooling interval, then starts a fresh work window. Cooling is an application-level pacing mechanism; it does not claim to change or defeat any external service limit.

## Future integration points

1. Surface Work/Cooling status in the UI.
2. Add manual Pause/Resume controls.
3. Add a per-monitor opt-in for development-plan automation.
4. Add a plan-step selector and a visible checkpoint history.
5. Add automated release/build validation for the feature.

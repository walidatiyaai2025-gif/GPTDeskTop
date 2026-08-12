# FLOW-001 — New Chat + Monitor workflow

Status: IN PROGRESS
Issue: #210

## Goal
Create a first-class operator workflow that opens a fresh ChatGPT conversation, sends an operator-defined bootstrap message, resolves the resulting stable conversation identity, creates a SavedMonitor with a second independent operator-defined auto-reply message, and starts that monitor immediately.

## Invariants
- Initial Chat Message and Monitor Auto Reply are independent values.
- The initial message must be delivered through the verified ChatGPT user-message receipt path.
- A monitor must never be registered against an unverified root/new-chat URL; the same Chrome target must expose a stable ChatGPT conversation URL first.
- Monitor registration remains duplicate-safe.
- The new monitor uses the existing global delay/timer/rotation/model-routing defaults.
- Desired-running state is persisted only after the monitor actually starts, so RST-001 can resume it after application restart.
- The last-used two messages are persisted in SQLite.
- Hidden Chrome preference is restored after the workflow.

## Validation
- Persist/reopen test for the two last-used messages.
- Source-order regression contract for create → verified send → stable identity → register → start → resume-intent.
- Main-window/dialog contract for the single operator action and two independent message fields.
- Existing full repository CI gates must remain green before merge.

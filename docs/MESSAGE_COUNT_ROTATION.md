# Message-Count Conversation Rotation

Tracking: #10

## Goal

Allow an operator to proactively move a running monitor to a fresh ChatGPT conversation after a configurable number of visible assistant responses, without waiting for a context-limit message.

## Settings

Global Settings provide:

- `RotateAfterAssistantMessages`: assistant-response threshold; `0` disables proactive message-count rotation.
- `MessageCountRotationStartMessage`: fixed message sent to the new ChatGPT conversation.

The existing per-monitor `ConversationRotationEnabled` switch remains the master enable. Existing per-monitor New Chat delay, rotation cooldown, maximum rotation count and model-routing settings also apply.

## Runtime Contract

When a stable, non-error, non-context-limit assistant response is present and the visible assistant response count reaches the configured threshold:

1. Confirm that conversation rotation is enabled and the monitor has not exceeded its maximum rotation count.
2. Wait the configured New Chat delay.
3. Open a fresh ChatGPT conversation and wait for the composer to become available.
4. Apply the monitor's existing model-routing policy.
5. Send the fixed `MessageCountRotationStartMessage` using verified delivery.
6. After verified delivery, re-enumerate the same Chrome target until it exposes the stable `/c/{conversation-id}` URL created by ChatGPT.
7. Commit the identity move through one immediate SQLite writer transaction: the old saved conversation must still match the monitor snapshot, the new stable conversation must be unowned, RotationCount is incremented, and the rotation + success receipts are written atomically under the same Monitor ID.
8. Close the old chat only after that transaction commits successfully.
8. Apply the existing rotation cooldown and continue monitoring.

If verified delivery fails, the new unused tab is closed, the old conversation remains authoritative, and the same rotation remains eligible for a later retry. If the post-send target never exposes a stable conversation URL, another monitor owns the new URL, or the source monitor binding changed concurrently, the new tab is left unclaimed/closed and the old tab is not closed by the handoff path.

## Compatibility

- Context-limit rotation remains unchanged and may still use `ConversationHandoffService` plus the per-monitor context-limit start message.
- Delivery-timeout recovery remains unchanged and takes precedence over proactive message-count rotation for error responses.
- A threshold of `0` preserves the v1.8.0 behavior.
- No SQLite schema migration is required because the new options are stored in the existing `AppSettings` table.

## Validation

`MessageCountRotationRegressionTests` locks the Settings keys/UI labels, threshold trigger, audit status names, verified-delivery ordering, same-monitor persistence and old-tab close ordering.

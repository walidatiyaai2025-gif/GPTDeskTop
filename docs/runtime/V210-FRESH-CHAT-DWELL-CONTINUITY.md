# GPTDeskTop v2.0.10 — Fresh Chat Dwell Continuity

## Field failure

In v2.0.9 a normal per-response fresh-chat handoff could succeed repeatedly and then stop before physical submit when the newly created blank ChatGPT target changed only its URL representation during the mandatory 15-second pre-send dwell. The runtime treated any exact URL-string change as a target navigation/rebind and failed closed.

A second control-flow defect made that safe pre-submit deferral terminal for the completed source response: the response was marked handled even though no composer mutation had occurred, so a later poll could not retry the fresh-chat handoff.

## v2.0.10 correction

- The 15-second dwell remains mandatory and still requires the exact same Chrome target ID.
- Durable `/c/{conversation-id}` ownership remains fail-closed and must resolve to the same conversation identity.
- A blank pre-first-turn ChatGPT target may canonicalize benign URL/query state without resetting the dwell while it remains the same target and has not unexpectedly acquired a different durable conversation identity.
- Query parameters are excluded from durable conversation identity comparison.
- Fresh-chat delivery now distinguishes `Accepted`, `DeferredBeforePhysicalSubmit`, and `ReconcileRequired`.
- A pre-submit deferral closes the unused fresh target and keeps the same completed source response eligible for a later safe fresh-chat retry.
- An uncertain post-authority delivery remains protected by the exactly-once fence: no blind resend, no destructive checkpoint clearing, and no blind target close.

## Safety invariants retained

- No automated continuation is sent back into the completed old conversation.
- Global serialized send authority remains in force.
- Mandatory 15-second pre-send dwell and inter-send cooldown remain in force.
- Physical-submit uncertainty remains fail-closed and exactly-once protected.

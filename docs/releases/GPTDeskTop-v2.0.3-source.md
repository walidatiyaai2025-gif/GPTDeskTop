# GPTDeskTop v2.0.3 — Final Runtime Source

This source revision closes the field-observed false-positive physical submit acknowledgement defect.

Runtime contract:

- A JavaScript `sendButton.click()` return value is not treated as proof that ChatGPT accepted a user turn.
- Immediate acceptance requires observable evidence: a matching new user turn, generation beginning, or an accepted composer transition.
- If the exact expected text remains in an enabled, send-ready composer, the click is classified as not accepted and may be retried only within a bounded three-click budget without consuming the exactly-once submit budget.
- Any ambiguous or transport-uncertain post-click state enters read-only reconciliation; blind resend remains prohibited.
- The global ChatGPT `Too many requests` / temporarily-limited modal is a hard composer interlock.
- Post-submit reconciliation remains reload-free; no reload storm is authorized by this release.
- Release identity is GPTDeskTop v2.0.3, including installer registry metadata.

Validation authority is the `Final Runtime Closure Validation` workflow on this exact pushed source revision.

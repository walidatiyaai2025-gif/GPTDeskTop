# MONLOCK-001 — Chat composer/send-button interlock

## Symptom
After an assistant response arrives, the ChatGPT send button can remain temporarily unavailable while GPTDeskTop automation is preparing or retrying the next continuation message.

## Evidence from operator diagnostics
The supplied runtime logs show repeated `send-storm-suppressed` events and, later, a short CDP endpoint outage followed by successful recovery. The monitor itself remains enabled/running. This makes repeated send preparation during transient composer state a high-risk path that must be gated before any DOM mutation.

## Root-risk in current send path
`ChromeDevToolsService.SendChatMessageAsync` currently writes/focuses the editor before it checks whether the send path is actually ready. `SendChatMessageVerifiedAsync` can retry that path during its verification deadline. Therefore a transient disabled/generating composer can receive repeated automation-side focus/input mutations even when no send should occur yet.

## Required behavior
1. Observe composer state using a read-only probe before touching the editor.
2. If ChatGPT is generating, editor is disabled/missing, or send is disabled/missing, wait passively; do not focus, select, inject text, click, press Enter, reload, or recreate the tab solely for that condition.
3. Mutate the composer only after the gate reports `ReadyToSend`.
4. After a send attempt, verify a new user turn before retrying.
5. Keep bounded retry/send-storm protection and CDP recovery independent from composer readiness.

## Implemented building blocks
- `ChatComposerInterlockPolicy` — deterministic decision gate.
- `ChatComposerReadinessScript` — read-only DOM readiness probe with no focus/input/click side effects.
- This contract documents the integration rule and diagnostic rationale.

## Follow-up integration
Wire the readiness probe into `SendChatMessageVerifiedAsync` before every call that can mutate the composer, then add runtime regression coverage proving zero editor mutations while generating/disabled.

# MONLOCK — ChatGPT composer interlock hardening

## Operator evidence
The operator reported that the ChatGPT send button became locked while a monitor-controlled conversation was receiving a response. Supplied runtime evidence also showed repeated send-storm suppression events around monitor activity.

## Root cause
The historical send path could focus/write the composer before final send readiness was known. During a long ChatGPT response, transient editor/send states therefore risked automation touching the composer while ChatGPT still owned the generation cycle.

## Production contract
Monitor automation must observe first and mutate only after a deterministic readiness gate is clear. A generating response, missing/disabled canonical editor, disabled/missing Send button, or current rendered error is a defer state. Defer states are passive waits and never justify reload/recreate by themselves.

## Completed hardening
- MONLOCK-001: diagnosis and operator evidence recorded.
- MONLOCK-002: deterministic `ChatComposerInterlockPolicy` added.
- MONLOCK-003: read-only `ChatComposerReadinessScript` added.
- MONLOCK-004: readiness gate wired before editor mutation and before submit.
- MONLOCK-005: generating/disabled-send states remain passive waits.
- MONLOCK-006: source regression coverage proves generation cannot reach editor mutation and synthetic Enter is absent.
- MONLOCK-007: recovery sequence test proves disabled Send transitions to exactly one ReadyToSend decision after generation ends.
- MONLOCK-008: 10,000-poll endurance test proves a long generating window never becomes mutation-ready.
- MONLOCK-009: runtime diagnostics expose only latest decision, stable reason code, and timestamp; prompt/message text is not recorded.
- MONLOCK-010: automation targets only `#prompt-textarea` / canonical textarea and never generic `[contenteditable=true]`, preserving unrelated/manual editable surfaces.

## Security/privacy note
Composer diagnostics intentionally contain no prompt, response, repository secret, token, or conversation body. They record only readiness state metadata.

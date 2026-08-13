# MON-RUNTIME-SAFETY-001

Status: Implemented on PR #257; merge only after normal PR gates are green.

Production symptoms addressed:
- the dedicated Chrome DevTools endpoint can stay unreachable and leave monitor discovery/recovery retrying indefinitely;
- a send-verification race can issue the same continuation multiple times before a new assistant turn exists.

Safety behavior:
- after four consecutive loopback CDP endpoint failures, use a cooldown-protected dedicated-Chrome relaunch and bounded endpoint probe;
- install a page-level at-most-once submit guard on ChatGPT tabs: one Send/Enter is allowed, further submits are suppressed until a new assistant turn has completed;
- write only operational metadata to `logs/monitor-runtime-safety.jsonl`; no prompt/response text, URL, title, or tab identity is exported.

Verification: `MonitorRuntimeSafetyRegressionTests` locks the endpoint-recovery and send-guard contracts.

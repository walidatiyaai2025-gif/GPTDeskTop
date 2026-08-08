# Conservative Model Routing

GPTDeskTop supports optional, per-monitor model routing for ChatGPT Web.

## Safety rules

1. Routing is **disabled by default**.
2. `Auto` means the application does not change the currently selected ChatGPT model.
3. Routing only applies when starting/recovering a conversation; it is not used to evade usage limits.
4. A failed model-selection attempt leaves the current model unchanged.
5. The monitor never performs rapid model hopping after a rate-limit or usage-limit response.
6. Transient Chrome/CDP errors use the existing bounded retry/backoff path.
7. A real usage/context limit remains a limit: conversation rotation is only triggered by an explicit UI signal, not by a guessed message counter.

## Per-monitor settings

- **Model routing**: off by default.
- **Preferred model label**: `Auto` by default.
- **Fallback model label**: `Auto` by default.

Labels are matched against the visible model picker. Because ChatGPT Web UI labels can change, an unmatched label is treated as a no-op rather than causing repeated retries.

## Recommended operating mode

For a single long-running monitor, keep routing disabled unless there is a concrete reason to select a different model. Use `Auto` for normal continuation and let ChatGPT apply the account's normal model availability and limits.

For explicit routing, configure a preferred label and an alternate label. The alternate is considered only during a new-chat/recovery transition and only when it is different from the preferred label.

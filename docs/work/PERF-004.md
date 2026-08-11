# PERF-004 — Reduce monitor hot-loop payload and SQLite polling

Issue: #165

Implementation branch: `agent/perf-004-monitor-hot-loop`

Status: **IMPLEMENTED / VALIDATION PENDING**

## Problem

Two avoidable costs remained in the one-second monitor hot loop after PERF-002 and PERF-003:

1. `GetChatStateAsync` extracted and transported the full growing assistant response on every poll while ChatGPT was still generating, even though passive-wait logic immediately discarded that text.
2. Every monitor reread `RotateAfterAssistantMessages` and `MessageCountRotationStartMessage` from SQLite on every poll.

For long responses and multiple monitors, both costs scale continuously with runtime and monitor count.

## Delivered contract

1. Generation/error detection and assistant message count are still evaluated on every poll.
2. While ChatGPT is generating, `lastAssistantText` is intentionally returned as an empty string so the growing response body is not serialized through CDP every second.
3. As soon as generation stops, the next poll reads the complete assistant response exactly as before and existing stable-response detection takes over.
4. Message-count rotation settings are loaded when the monitor worker starts and refreshed at most once every 5 seconds instead of on every poll.
5. Runtime setting changes still become effective without restarting the monitor, with a bounded refresh delay of up to five seconds plus the current poll interval.
6. Passive long-response waiting, explicit error recovery, auto reply, context/message-count rotation, handoff, model routing and saved monitor identity behavior are unchanged.

## Regression coverage

`MonitorHotLoopPerformanceRegressionTests` locks:

- generation detection before assistant-body extraction,
- suppression of the streaming assistant payload,
- full-text retrieval remaining enabled after generation ends,
- the five-second runtime settings refresh contract,
- and the absence of per-poll SQLite setting reads.

## Validation

GitHub Actions validation is required on the implementation PR before merge.
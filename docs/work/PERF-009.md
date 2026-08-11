# PERF-009 — Stagger multi-monitor polling phases to reduce synchronized bursts

Issue: #184 — **Closed / Completed**

Branch: `agent/perf-009-poll-stagger`

PR: #186

Baseline main: `dee81e229ddd794c0672d589d67b5ad34ec6e03e`

Verified PR head: `fe6bb197ee5c82e6a68d42ebd97361d9ecba0781`

Squash merge to `main`: `c19a00fac45e93f7e7d69c4bacd5b0a43052b841`

Status: **DONE / VERIFIED / MERGED**

## Problem

Each monitor correctly owns its configured `PeriodicTimer`, but monitors started together with the same interval tend to wake on nearly the same phase. With several one-second workers this creates recurring CPU/CDP bursts even when average work is modest.

## Delivered

- Added deterministic `MonitorPollScheduler.GetInitialStagger` based on stable Monitor ID.
- The initial ChatGPT state snapshot remains immediate and happens before any stagger.
- Only the repeating poll cadence is phase-shifted.
- The configured poll period itself is unchanged.
- One-second monitors receive at most an 800ms phase offset.
- Longer intervals can spread up to 2 seconds, never more.
- The scheduler is deterministic across calls for the same monitor/period and requires no shared mutable state.
- No random generator, timer registry, or background scheduler thread was introduced.

## Regression coverage

`MonitorPollSchedulerTests` verifies:

- 64 one-second monitors distribute across the 0–800ms window with high phase diversity,
- long-period monitors remain bounded by 2 seconds,
- invalid monitor IDs/periods do not delay,
- initial state acquisition occurs before the stagger,
- `PeriodicTimer` still uses the unchanged configured period.

The deterministic distribution was also checked across monitor IDs 1–64: 61 distinct one-second phases and 63 distinct 30-second phases.

## Compatibility

- Passive long-response waiting and error-driven recovery are unchanged.
- Initial state acquisition remains immediate.
- Rotation, auto reply, handoff, model routing, saved monitor identity, PERF-006 chat cache, PERF-007 SQLite retention, and PERF-008 shared settings snapshot are preserved.
- No schema, UI, release artifact, CDP transport, or response-detection changes.

## Verification receipts

All eight established GitHub Actions workflows passed on exact final head `fe6bb197ee5c82e6a68d42ebd97361d9ecba0781`:

- Build GPTDeskTop #604 — Success
- QA Release x64 #392 — Success
- QA Hidden Chrome CDP #374 — Success
- QA Passive Chat Wait #368 — Success
- QA Crash Process Recovery #382 — Success
- Development Delivery Receipts #482 — Success
- Development Task Recovery #478 — Success
- Development Message Reload #309 — Success

PR #186 was squash-merged to `main` as `c19a00fac45e93f7e7d69c4bacd5b0a43052b841`, and issue #184 closed as Completed.

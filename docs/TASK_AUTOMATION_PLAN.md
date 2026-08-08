# Task Automation Implementation Plan

## Phase 1 — Core execution

- [x] Editable 10-message development-plan catalog.
- [x] One message per work cycle; messages are not sent as a batch.
- [x] 10-minute bounded work window.
- [x] 5-minute cooling window.
- [x] Persistent message index and per-monitor checkpoints.
- [x] Resume sequence after application startup.
- [x] Re-resolve saved monitors and their current Chrome Tab IDs each cycle.
- [x] Record send result and checkpoint in SQLite history/settings.

## Phase 2 — Runtime integration

- [ ] Surface Working/Cooling/Paused state in the main UI.
- [ ] Add Pause/Resume/Run-now controls.
- [ ] Add visible current message and next message preview.
- [ ] Add per-monitor opt-in.
- [ ] Restore open-chat targets after a normal restart when the saved tab is unavailable.

## Phase 3 — Validation

- [ ] Release build on Windows x64.
- [ ] Verify startup resume from a persisted checkpoint.
- [ ] Verify one message is delivered per work cycle.
- [ ] Verify cooling survives process restart without resetting the message index.
- [ ] Verify 10-message catalog can be edited without recompiling.

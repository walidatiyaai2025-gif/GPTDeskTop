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

- [x] Surface Working/Cooling/Paused/Stopped/Faulted state in the main automation control UI.
- [x] Add Pause/Resume/Run-now/Stop controls.
- [x] Add visible current message and next message preview.
- [x] Add explicit per-monitor opt-in; non-opted-in monitors are excluded by the worker.
- [x] Persist per-monitor development plan ID and title.
- [x] Reload the editable message catalog at every work cycle without restarting the application.
- [ ] Restore open-chat targets after a normal restart when the saved tab is unavailable.

## Phase 3 — Validation

- [ ] Release build on Windows x64.
- [ ] Verify startup resume from a persisted checkpoint.
- [ ] Verify one message is delivered per work cycle.
- [ ] Verify cooling survives process restart without resetting the message index.
- [ ] Verify 10-message catalog can be edited without recompiling.
- [ ] Verify unchecked monitors never receive development-plan messages.
- [ ] Verify plan ID/title placeholders are resolved per monitor.

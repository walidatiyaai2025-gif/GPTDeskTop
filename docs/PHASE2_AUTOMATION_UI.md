# Automation Control Center — Phase 2

The development automation control center provides explicit runtime controls over the existing checkpointed worker.

## Controls

- **Run Now**: cancels the current worker, starts a fresh work-window checkpoint, and resumes the persisted message indexes.
- **Pause**: cancels the worker and records `Paused` without deleting per-monitor checkpoints.
- **Resume**: restores `Working` state and starts the worker from the persisted per-monitor message indexes.
- **Stop**: cancels the worker and records `Stopped`; checkpoints remain intact.

## Runtime visibility

The control center displays:

- Current phase (`Working`, `Cooling`, `Paused`, `Stopped`, `Faulted`)
- Remaining work/cooling window
- Last cycle checkpoint
- Last cycle send count
- Worker state

## Safety

Run Now and Resume never reset the per-monitor message index. They only change the worker lifecycle and window timestamp. The pacing mechanism remains cooperative and does not attempt to bypass service quotas or access controls.

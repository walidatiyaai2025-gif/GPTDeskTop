# UI-CMD-002 — Compact Commands completeness

## Status
IN PROGRESS — implementation complete on branch; awaiting CI/merge.

## Reason
The compact top `Commands` menu intentionally hides the legacy main toolbar to reclaim vertical workspace. During verification, the existing `New Chat + Monitor` workflow was found to be present on the hidden toolbar but absent from the compact menu, making that operator path inaccessible.

## Scope
- Restore `New Chat + Monitor` under `Commands > Monitors`.
- Keep the original hidden `Button` and its existing click/event handler as the single behavior owner.
- Add the action to `CommandSources` discovery and `IsComplete` gating so installation fails closed if a future hidden action cannot be resolved.
- Add regression coverage for the restored command.

## Files
- `src/GPTDeskTop/UI/CompactTopCommandMenuExperience.cs`
- `tests/GPTDeskTop.RuntimeTests/CompactTopCommandMenuUiRegressionTests.cs`

## Verification gates
- Full RuntimeTests suite.
- Build GPTDeskTop.
- QA Release x64.
- Existing passive-chat, hidden-CDP, crash-recovery and development-delivery gates.

## Coordination
Issue: #213
Branch: `codex/restore-new-chat-monitor-command`

No runtime monitoring/recovery/business logic is modified by this task.
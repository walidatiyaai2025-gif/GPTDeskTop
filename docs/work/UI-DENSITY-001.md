# UI-DENSITY-001 — Compact main operator header

## Status
IN PROGRESS — implementation complete on branch; awaiting CI/merge.

## Goal
Reclaim main-window vertical space for the operational workspace and Live Activity without removing any live status metric or changing monitor/runtime behavior.

## Implementation
- Added a presentation-only compact-header layer that runs after the existing idle presentation pass via `BeginInvoke`.
- Reduced the logical header row from the legacy 82px footprint to a DPI-scaled 58px compact header.
- Kept `GPTDeskTop` identity and the four live metric chips: Running, Monitors, Conversation tabs, Chrome window.
- Hid only the descriptive subtitle in the compact main-window presentation.
- Reduced metric-chip logical minimum size to 100x40 and tightened padding/margins.
- Reapplies sizing on DPI changes without timers or business-layer polling.

## Files
- `src/GPTDeskTop/UI/CompactOperatorHeaderExperience.cs`
- `tests/GPTDeskTop.RuntimeTests/CompactOperatorHeaderUiRegressionTests.cs`

## Safety boundaries
- No `ChatGptMonitorService` dependency.
- No `ChromeDevToolsService` dependency.
- No `LocalDatabase` dependency.
- No monitor start/stop/save behavior.
- Existing dashboard metrics remain the sole live-data owners.

## Coordination
Issue: #215
Branch: `codex/compact-operator-header`

# UI-POLISH-006 — Main window dock order and action visibility

## Status
IN PROGRESS

## Priority
High

## Problem reported
The primary GPTDeskTop window can render with Development Plan, Runtime Health and Stored History surfaces visually overlapping the `DockStyle.Fill` main workspace. This hides the main header/action bar and clips section headings. Several action buttons also appear as truncated labels such as `Refr...`, `Rep...`, `Det...` and `Colla...`.

## Root cause
1. `Program.cs` adds the top/bottom operational controls and moves each to child index `0`. WinForms calculates docking from reverse z-order, so the Fill workspace can consume the full client rectangle before edge-docked controls reserve space. The edge controls then paint over the workspace.
2. `SecondaryScreenExperience.ApplyRuntimeHeaderResponsive` reduced Runtime Health action columns to `72` logical pixels on compact layouts while Fluent button padding/margins and DPI scaling require more room.
3. Development Plan and long History/main actions did not have text-aware minimum widths in the responsive presentation layer.

## Implementation
- Normalize direct-child z-order for `MainForm` at the existing idle/responsive presentation boundary.
- Keep the Fill workspace at child index 0, then History, Support Diagnostics, Runtime Health and Development Plan so top/bottom surfaces reserve actual layout space.
- Add main-window button minimum widths for browser, monitor, runtime and selected-monitor actions.
- Add dedicated Development Plan responsive treatment and readable widths for Start, Pause, Resume, Stop, Messages, Schedule and Collapse/Details.
- Stop Runtime Health from shrinking action columns to text-truncating widths; use DPI-aware widths that include Fluent margins.
- Make History long actions (`Copy Selected`, `Export Visible CSV`) and Collapse/History toggle text-aware.
- Disable action-button ellipsis where the layout is expected to allocate sufficient width.
- Preserve all monitoring, Chrome/CDP, recovery, persistence and development-task behavior; changes are presentation/layout only.

## Regression coverage
`MainWindowLayoutVisibilityRegressionTests` locks:
- Main-window dock-order normalization.
- Development Plan readable action widths.
- Runtime Health non-truncating action columns.
- Main/History long-action minimum widths and no-ellipsis contract.

## Acceptance criteria
- Main GPTDeskTop header and toolbar are never hidden underneath Development Plan or Runtime Health.
- Open ChatGPT Conversations and Saved Monitors headings remain fully visible.
- Runtime Health buttons display complete labels: Refresh, Repair…, Retry and Details/Collapse.
- Development Plan displays complete labels: Start, Pause, Resume, Stop, Messages, Schedule and Details/Collapse.
- Main toolbar and Selected Monitor primary action remain fully readable.
- Stored History Explorer toggle and long filter/export actions remain fully readable.
- No horizontal overflow is introduced at the supported main-window minimum size.
- Existing runtime/business semantics remain unchanged.

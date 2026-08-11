# UI-POLISH-006 — Main window dock order and action visibility

Branch: `agent/ui-layout-visibility-fix`

PR: #185

Baseline main: `6d4260e0451c33d79278d313dc0d345bf6e3400a`

Verified PR head: `4219de12324d724189bc53849df47de426fd2039`

Squash merge to `main`: `c44c6d79fdab2f92b4d7e0bd0037daebc843d16a`

Status: **DONE / VERIFIED / MERGED**

## Priority
High

## Problem reported
The primary GPTDeskTop window could render with Development Plan, Runtime Health and Stored History surfaces visually overlapping the `DockStyle.Fill` main workspace. This hid the main header/action bar and clipped section headings. Several action buttons also appeared as truncated labels such as `Refr...`, `Rep...`, `Det...` and `Colla...`.

## Root cause
1. `Program.cs` adds the top/bottom operational controls and moves each to child index `0`. WinForms calculates docking from reverse z-order, so the Fill workspace could consume the full client rectangle before edge-docked controls reserved space. The edge controls then painted over the workspace.
2. `SecondaryScreenExperience.ApplyRuntimeHeaderResponsive` reduced Runtime Health action columns to `72` logical pixels on compact layouts while Fluent button padding/margins and DPI scaling required more room.
3. Development Plan and long History/main actions did not have text-aware minimum widths in the responsive presentation layer.

## Delivered
- Normalized direct-child z-order for `MainForm` at the existing idle/responsive presentation boundary.
- Kept the Fill workspace at child index 0, then History, Support Diagnostics, Runtime Health and Development Plan so top/bottom surfaces reserve actual layout space.
- Added main-window button minimum widths for browser, monitor, runtime and selected-monitor actions.
- Added dedicated Development Plan responsive treatment and readable widths for Start, Pause, Resume, Stop, Messages, Schedule and Collapse/Details.
- Stopped Runtime Health from shrinking action columns to text-truncating widths; action columns now use DPI-aware widths that include Fluent margins.
- Made History long actions (`Copy Selected`, `Export Visible CSV`) and Collapse/History toggle text-aware.
- Disabled action-button ellipsis where the layout allocates sufficient width.
- Preserved monitoring, Chrome/CDP, recovery, persistence and development-task behavior; this sprint changed presentation/layout only.

## Regression coverage
`MainWindowLayoutVisibilityRegressionTests` locks:
- Main-window dock-order normalization.
- Development Plan readable action widths.
- Runtime Health non-truncating action columns.
- Main/History long-action minimum widths and no-ellipsis contract.

The existing secondary-screen responsive regression contract was reconciled to the intentional Runtime Health breakpoints (`1080px` compact and `900px` very compact) after the first CI pass correctly detected the old `930px`/`760px` expectations.

## Verification receipts
All eight established GitHub Actions workflows passed on exact final head `4219de12324d724189bc53849df47de426fd2039`:

- Build GPTDeskTop #605 — Success
- QA Release x64 #393 — Success
- QA Hidden Chrome CDP #375 — Success
- QA Passive Chat Wait #369 — Success
- QA Crash Process Recovery #383 — Success
- Development Delivery Receipts #483 — Success
- Development Task Recovery #479 — Success
- Development Message Reload #310 — Success

The runtime suite, application build and setup build all passed before merge. PR #185 was squash-merged to `main` as `c44c6d79fdab2f92b4d7e0bd0037daebc843d16a`.

## Acceptance result
- Main GPTDeskTop header and toolbar are protected from Development Plan / Runtime Health overlap.
- Open ChatGPT Conversations and Saved Monitors headings remain visible in the reserved main workspace.
- Runtime Health buttons display complete labels: Refresh, Repair…, Retry and Details/Collapse.
- Development Plan displays complete labels: Start, Pause, Resume, Stop, Messages, Schedule and Details/Collapse.
- Main toolbar and Selected Monitor primary action have text-aware minimum widths.
- Stored History Explorer toggle and long filter/export actions have text-aware minimum widths.
- Responsive breakpoints hide secondary Runtime Health text before shrinking action columns into truncation.
- Existing runtime/business semantics remain unchanged.

# UI-POLISH-004 — 100 main dashboard operations-console refinements

## Status
IMPLEMENTED / VALIDATION PENDING

## Tracking
- Issue: #171
- Branch: `agent/ui-polish-004-main-dashboard`
- Baseline main: `d9b687410f6c13b69254778e4d4311f990827b38`

## Goal
Refine the Main Dashboard into a clearer operations console while preserving all monitoring, recovery, Chrome/CDP, persistence, task-engine and release semantics.

## Completed microtasks

### 001–010 — Dashboard shell
- [x] 001 Add a dashboard-only experience layer.
- [x] 002 Bootstrap it automatically through the WinForms idle loop.
- [x] 003 Restrict activation to `MainForm` only.
- [x] 004 Use weak form registration.
- [x] 005 Use weak control registration.
- [x] 006 Keep application behavior dependencies out of the layer.
- [x] 007 Dispose dashboard tooltips on form close.
- [x] 008 Make dashboard enhancement idempotent.
- [x] 009 Re-apply responsive rules after resize.
- [x] 010 Add a dashboard-level accessible description.

### 011–020 — Root composition and header
- [x] 011 Increase root workspace breathing room.
- [x] 012 Normalize root background to the Fluent canvas.
- [x] 013 Describe the dashboard layout for accessibility tools.
- [x] 014 Strengthen the GPTDeskTop title semantics.
- [x] 015 Give the title an explicit accessible name.
- [x] 016 Clarify the title accessibility description.
- [x] 017 Strengthen subtitle contrast.
- [x] 018 Clarify the subtitle purpose.
- [x] 019 Raise the header surface visually.
- [x] 020 Round the header card consistently.

### 021–030 — Metric chips
- [x] 021 Detect the Running metric chip.
- [x] 022 Detect the Monitors metric chip.
- [x] 023 Detect the Conversation tabs metric chip.
- [x] 024 Detect the Chrome window metric chip.
- [x] 025 Normalize metric chip padding.
- [x] 026 Normalize metric chip spacing.
- [x] 027 Enforce a useful metric chip minimum size.
- [x] 028 Round metric chip surfaces.
- [x] 029 Add metric tooltips.
- [x] 030 Add metric accessibility names.

### 031–040 — Live metric semantics
- [x] 031 Color active Running counts as success.
- [x] 032 De-emphasize zero Running counts.
- [x] 033 Color non-zero monitor totals as informational.
- [x] 034 Color non-zero conversation totals as informational.
- [x] 035 Treat visible Chrome as healthy/success.
- [x] 036 Treat hidden Chrome as warning/attention.
- [x] 037 Keep unknown Chrome state neutral.
- [x] 038 React to metric text changes live.
- [x] 039 Hook metric text-change handlers idempotently.
- [x] 040 Preserve metric business values unchanged.

### 041–050 — Command groups
- [x] 041 Detect the Browser command group.
- [x] 042 Detect the Monitor command group.
- [x] 043 Detect the Runtime command group.
- [x] 044 Detect the App command group.
- [x] 045 Put command groups on clean surfaces.
- [x] 046 Normalize command-group padding.
- [x] 047 Normalize command-group margins.
- [x] 048 Round command-group containers.
- [x] 049 Improve command-group caption contrast.
- [x] 050 Add command-group accessibility descriptions.

### 051–060 — Primary actions and tooltips
- [x] 051 Keep Launch Chrome visually primary.
- [x] 052 Keep Start All visually primary.
- [x] 053 Keep Delete visually destructive.
- [x] 054 Normalize dashboard button height.
- [x] 055 Give long actions more minimum width.
- [x] 056 Normalize button spacing.
- [x] 057 Add Launch Chrome operator guidance.
- [x] 058 Add Refresh/F5 guidance.
- [x] 059 Add Add Monitor/Ctrl+N guidance.
- [x] 060 Add Settings/Ctrl+, guidance.

### 061–070 — Sections and selected monitor
- [x] 061 Detect Open ChatGPT Conversations section.
- [x] 062 Detect Saved Monitors section.
- [x] 063 Detect Selected Monitor section.
- [x] 064 Detect Live Activity section.
- [x] 065 Detect Stored History section.
- [x] 066 Strengthen section title semantics.
- [x] 067 Normalize section card padding.
- [x] 068 Round section card surfaces.
- [x] 069 Improve section subtitle contrast.
- [x] 070 Protect section subtitles with ellipsis.

### 071–080 — Selected monitor context
- [x] 071 Raise the selected-monitor summary surface.
- [x] 072 Add selected-monitor accessibility metadata.
- [x] 073 Style the read-only auto-reply summary distinctly.
- [x] 074 Label the auto-reply field as read-only for accessibility.
- [x] 075 Label selected monitor enabled state clearly.
- [x] 076 Keep Edit Selected Monitor visually primary.
- [x] 077 Enforce usable Edit Selected Monitor width.
- [x] 078 Add Ctrl+E guidance to quick edit.
- [x] 079 Highlight the no-selection summary state.
- [x] 080 Preserve selected-monitor data as read-only presentation.

### 081–090 — Grid scanability and activity
- [x] 081 Use borderless dashboard grids.
- [x] 082 Use subtle horizontal row separators.
- [x] 083 Normalize grid row height.
- [x] 084 Normalize grid header height.
- [x] 085 Disable OS header visual overrides.
- [x] 086 Add header tooltips.
- [x] 087 Center compact operational columns.
- [x] 088 Give each dashboard grid a semantic accessible name.
- [x] 089 Darken Live Activity for stronger console contrast.
- [x] 090 Keep Live Activity non-wrapping with two-axis scrolling.

### 091–100 — Empty states, footer and responsive safety
- [x] 091 Strengthen conversation empty-state guidance.
- [x] 092 Strengthen monitor empty-state guidance.
- [x] 093 Strengthen history empty-state guidance.
- [x] 094 Normalize empty-state padding.
- [x] 095 Clarify build/version footer accessibility.
- [x] 096 Increase footer contrast slightly.
- [x] 097 Wrap command flows on compact dashboards.
- [x] 098 Reduce command-group spacing on compact dashboards.
- [x] 099 Add regression coverage for UI-only dashboard scope.
- [x] 100 Add regression guards against persistence/runtime mutation from the dashboard layer.

## Scope boundary
Implementation is intentionally isolated to `src/GPTDeskTop/UI/MainDashboardExperience.cs`, UI regression coverage, and this receipt. It does not call monitoring, recovery, Chrome/CDP, database, task-engine, setup or release services.

## Validation
Pending exact-head GitHub Actions validation.
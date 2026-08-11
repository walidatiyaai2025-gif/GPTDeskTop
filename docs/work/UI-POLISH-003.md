# UI-POLISH-003 — 100 screen-level UX refinements

## Status
IMPLEMENTED / VALIDATION PENDING

## Tracking
- Issue: #167
- Branch: `agent/ui-polish-003-screen-experience`
- Baseline main: `2257b03503120cc97ca363548b06e3305c9fa68c`

## Goal
Add one hundred screen-level operator-experience refinements on top of the shared Fluent theme without changing monitoring, recovery, Chrome/CDP, SQLite, scheduling, task-engine or release semantics.

## Completed microtasks

### 001–010 — Application shell and lifecycle
- [x] 001 Add a dedicated screen-experience layer independent from business services.
- [x] 002 Bootstrap the layer automatically when the WinForms assembly loads.
- [x] 003 Apply enhancements through the UI message-loop Idle boundary.
- [x] 004 Discover every currently open form without hard-coding form instances.
- [x] 005 Use weak form registration so closed forms can be collected.
- [x] 006 Use weak control registration for per-control enhancement metadata.
- [x] 007 Make each form enhancement idempotent.
- [x] 008 Dispose the form-owned experience ToolTip on FormClosed.
- [x] 009 Ignore disposed/disposing forms during enhancement passes.
- [x] 010 Re-run responsive layout safely after form resize.

### 011–020 — Dynamic UI and accessibility foundation
- [x] 011 Observe ControlAdded so dynamically-created controls inherit the experience layer.
- [x] 012 Make dynamic-child event registration idempotent.
- [x] 013 Enhance nested dynamic children recursively.
- [x] 014 Re-apply responsive rules after dynamic child insertion.
- [x] 015 Preserve existing explicit AccessibleName values.
- [x] 016 Append keyboard guidance to form accessibility descriptions.
- [x] 017 Add workspace navigation guidance to tab controls.
- [x] 018 Give unnamed tabs usable accessibility names.
- [x] 019 Give unnamed tab pages usable accessibility descriptions.
- [x] 020 Keep the new layer free of monitor/CDP/database dependencies.

### 021–030 — Search and filter workflow
- [x] 021 Detect search fields by placeholder text.
- [x] 022 Detect search fields by accessible name.
- [x] 023 Detect search fields by accessible description.
- [x] 024 Enforce a more usable minimum width for undocked search fields.
- [x] 025 Add Ctrl+F guidance to search fields.
- [x] 026 Add Ctrl+Shift+F clear-search guidance.
- [x] 027 Add a concise search shortcut tooltip.
- [x] 028 Detect filter ComboBoxes through accessibility metadata.
- [x] 029 Increase filter dropdown item capacity for faster scanning.
- [x] 030 Keep filter dropdown width at least as wide as the field.

### 031–040 — Keyboard accelerators
- [x] 031 Enable KeyPreview on enhanced forms.
- [x] 032 Ctrl+F focuses and selects the active workspace search field.
- [x] 033 Ctrl+Shift+F clears and focuses the active workspace search field.
- [x] 034 F5 invokes the first visible enabled Refresh action.
- [x] 035 Ctrl+S invokes the form Accept/Save action.
- [x] 036 Ctrl+E invokes the first visible enabled Export action.
- [x] 037 Ctrl+Shift+B invokes Create Support Bundle when available.
- [x] 038 F6 advances focus between major tab-stop regions.
- [x] 039 Shift+F6 moves focus backward between major regions.
- [x] 040 Suppress handled shortcut keystrokes so controls do not receive duplicate input.

### 041–050 — Tab navigation
- [x] 041 Enable hot tracking on tab controls.
- [x] 042 Keep tab controls keyboard focusable.
- [x] 043 Keep tab rows single-line for predictable navigation.
- [x] 044 Make tab pages independently scrollable when content is constrained.
- [x] 045 Add an inner auto-scroll margin to tab pages.
- [x] 046 Ctrl+Tab moves to the next tab.
- [x] 047 Ctrl+Shift+Tab moves to the previous tab.
- [x] 048 Ctrl+PageDown moves to the next tab.
- [x] 049 Ctrl+PageUp moves to the previous tab.
- [x] 050 Alt+1 through Alt+9 jumps directly to the corresponding tab.

### 051–060 — Action clarity
- [x] 051 Preserve the form Accept button as a primary Fluent action.
- [x] 052 Promote Launch Chrome to a primary action when encountered.
- [x] 053 Promote Start All to a primary action when encountered.
- [x] 054 Promote Export Visible to a primary action when encountered.
- [x] 055 Promote Create Support Bundle to a primary action when encountered.
- [x] 056 Preserve Delete actions as danger actions.
- [x] 057 Preserve Remove actions as danger actions.
- [x] 058 Enforce a 36px minimum button height for screen-level actions.
- [x] 059 Add ellipsis protection to constrained action labels.
- [x] 060 Add visible shortcut hints through button tooltips/accessibility descriptions without bloating button text.

### 061–070 — Status, loading and health communication
- [x] 061 Detect status labels by AccessibleRole.StatusBar.
- [x] 062 Detect status labels by accessible status naming.
- [x] 063 Detect health labels by accessibility naming.
- [x] 064 Detect result-summary status labels.
- [x] 065 Give status labels consistent strong typography.
- [x] 066 Give status labels consistent breathing room.
- [x] 067 Map error/failed/blocked/invalid states to danger presentation.
- [x] 068 Map running/healthy/ready/connected/success states to success presentation.
- [x] 069 Map checking/loading/creating/working states to informational presentation.
- [x] 070 Map pending/stopped/unknown/deferred/retry states to warning presentation.

### 071–080 — Empty states, headers and URLs
- [x] 071 Detect empty states beginning with “No …”.
- [x] 072 Detect selection-required empty states beginning with “Select a …”.
- [x] 073 Detect explanatory empty states containing “will appear here”.
- [x] 074 Give empty states a softer alternate surface.
- [x] 075 Strengthen empty-state text readability.
- [x] 076 Increase empty-state padding for calmer composition.
- [x] 077 Detect large form headers and keep their visual priority.
- [x] 078 Detect key workspace headers such as History, Runtime Health and Support Diagnostics.
- [x] 079 Render visible conversation URLs in a compact monospaced treatment.
- [x] 080 Surface sensitive-data notices with warning emphasis.

### 081–090 — Grid and activity-log ergonomics
- [x] 081 Keep DataGridView cell tooltips enabled for truncated content.
- [x] 082 Copy grid selections with headers through Ctrl+C.
- [x] 083 Allow operators to reorder grid columns.
- [x] 084 Allow operators to resize grid columns.
- [x] 085 Keep row resizing disabled for stable scan density.
- [x] 086 Center compact ID/runtime/delay/poll columns for faster scanning.
- [x] 087 Ctrl+Home jumps to the first grid row.
- [x] 088 Ctrl+End jumps to the last grid row.
- [x] 089 Enable URL detection in read-only RichTextBox diagnostics/activity surfaces.
- [x] 090 Use non-wrapping two-axis scrolling for monospaced activity/log surfaces.

### 091–100 — Responsive layout and regression safety
- [x] 091 Let multi-action FlowLayoutPanels wrap instead of clipping controls.
- [x] 092 Normalize action spacing inside multi-button flows.
- [x] 093 Keep splitters user-adjustable.
- [x] 094 Enforce a minimum visible splitter width.
- [x] 095 Keep splitters out of the keyboard tab sequence.
- [x] 096 Reduce tab padding on compact windows.
- [x] 097 Restore comfortable tab padding on wider windows.
- [x] 098 Wrap action bars automatically on compact windows or large action sets.
- [x] 099 Add source-contract regression coverage for shortcuts, semantic states and weak lifecycle registration.
- [x] 100 Add a regression guard proving the screen-experience layer contains no ChatGptMonitorService, ChromeDevToolsService or LocalDatabase dependency.

## Scope boundary
The implementation lives in `src/GPTDeskTop/UI/ScreenExperience.cs` plus UI regression coverage. It does not mutate monitoring semantics, response detection, recovery, CDP transport, persistence, task scheduling, setup or release behavior.

## Validation
Pending exact-head GitHub Actions validation.
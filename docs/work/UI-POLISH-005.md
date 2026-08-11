# UI-POLISH-005 — Secondary screens DPI and responsive final pass

## Status
IMPLEMENTED / VALIDATION PENDING

## Tracking
- Issue: #174
- Branch: `agent/ui-polish-005-secondary-final-pass`
- Baseline main: `b39c1f99056f884665c8821a125c8212a01c80f6`

## Goal
Close the remaining core visual-polish gaps across secondary operator screens while preserving all monitoring, recovery, Chrome/CDP, persistence, task-engine, instance-handoff, setup and release semantics.

## Completed microtasks

### 001–010 — Secondary experience foundation
- [x] 001 Add a dedicated secondary-screen experience layer.
- [x] 002 Bootstrap through the WinForms idle boundary.
- [x] 003 Discover secondary controls across all open forms.
- [x] 004 Use weak control registration.
- [x] 005 Keep responsive hooks idempotent.
- [x] 006 Reapply screen treatment after size changes.
- [x] 007 Reapply form treatment after DPI changes.
- [x] 008 Scale final-pass dimensions from DeviceDpi.
- [x] 009 Preserve existing explicit accessibility metadata.
- [x] 010 Keep the final-pass layer free of business-service dependencies.

### 011–020 — Application Settings composition
- [x] 011 Detect Application Settings explicitly.
- [x] 012 Increase wide-screen root breathing room.
- [x] 013 Reduce root padding on compact settings windows.
- [x] 014 Strengthen Application Settings header semantics.
- [x] 015 Clarify global-settings accessibility purpose.
- [x] 016 Give wide tabs comfortable label padding.
- [x] 017 Reduce tab padding on compact settings windows.
- [x] 018 Make every settings tab independently scrollable.
- [x] 019 Add high-DPI auto-scroll margins to tab pages.
- [x] 020 Preserve the Fluent background hierarchy.

### 021–030 — Application Settings actions and states
- [x] 021 Preserve Save Settings as the primary action.
- [x] 022 Enforce a DPI-aware Save Settings hit target.
- [x] 023 Enforce a DPI-aware Cancel hit target.
- [x] 024 Reduce visual competition from Export Configuration Backup.
- [x] 025 Give backup actions consistent minimum sizing.
- [x] 026 Allow multi-button settings flows to wrap when compact.
- [x] 027 Keep wide settings action flows single-line.
- [x] 028 Upgrade settings status to semantic presentation.
- [x] 029 React to asynchronous settings status changes live.
- [x] 030 Elevate the sensitive-backup warning into a dedicated warning surface.

### 031–040 — Monitor Settings composition
- [x] 031 Detect Monitor Settings explicitly.
- [x] 032 Increase wide monitor-settings root breathing room.
- [x] 033 Reduce root padding on compact monitor-settings windows.
- [x] 034 Resize the runtime-status header column responsively.
- [x] 035 Preserve monitored-chat title space on compact windows.
- [x] 036 Give monitor tabs comfortable wide padding.
- [x] 037 Reduce monitor-tab padding when compact.
- [x] 038 Make monitor tabs independently scrollable.
- [x] 039 Add DPI-aware scroll margins to monitor tabs.
- [x] 040 Clarify tab navigation through accessibility metadata.

### 041–050 — Monitor Settings actions and dependent controls
- [x] 041 Preserve Save Monitor as the primary action.
- [x] 042 Enforce a DPI-aware Save Monitor hit target.
- [x] 043 Enforce a DPI-aware Cancel hit target.
- [x] 044 Detect and style the runtime status pill semantically.
- [x] 045 React to monitor runtime status changes live.
- [x] 046 Normalize checkbox vertical rhythm.
- [x] 047 De-emphasize disabled dependent checkboxes.
- [x] 048 Clarify Auto model-routing fields for accessibility.
- [x] 049 Preserve all dependent-control enable/disable logic untouched.
- [x] 050 Keep monitor save/runtime semantics presentation-only.

### 051–060 — Runtime Health responsive header
- [x] 051 Detect Runtime Health explicitly.
- [x] 052 Normalize Runtime Health outer DPI padding.
- [x] 053 Raise the health frame surface visually.
- [x] 054 Normalize health frame inner padding.
- [x] 055 Detect the eight-column health header.
- [x] 056 Shrink fixed health-header widths on compact screens.
- [x] 057 Hide Last Checked when compact space is constrained.
- [x] 058 Restore Last Checked automatically on wider screens.
- [x] 059 Hide verbose health summary only at very compact widths.
- [x] 060 Restore the health summary automatically when space returns.

### 061–070 — Runtime Health metrics and status
- [x] 061 Preserve the five health metric cards.
- [x] 062 Normalize metric-block top spacing.
- [x] 063 Add ellipsis protection to health metric labels.
- [x] 064 Keep health metric business values unchanged.
- [x] 065 Enforce DPI-aware action-button heights.
- [x] 066 Enforce minimum compact health-action widths.
- [x] 067 Upgrade overall health state to semantic status presentation.
- [x] 068 Map health failures to danger treatment.
- [x] 069 Map healthy/ready states to success treatment.
- [x] 070 Map checking/retry/pending states to information or warning treatment.

### 071–080 — Stored History responsive workspace
- [x] 071 Detect Stored History Explorer explicitly.
- [x] 072 Normalize History outer DPI padding.
- [x] 073 Shrink History title width on compact screens.
- [x] 074 Shrink History toggle width on compact screens.
- [x] 075 Preserve wide History header proportions.
- [x] 076 Make the filter bar wrap naturally.
- [x] 077 Remove nested filter-bar scrolling.
- [x] 078 Switch filter-row height to AutoSize when compact.
- [x] 079 Restore the normal fixed filter-row height on wider screens.
- [x] 080 Resize the history search field responsively.

### 081–090 — Stored History controls and grid
- [x] 081 Keep filter ComboBoxes usable at high DPI.
- [x] 082 Increase filter dropdown capacity.
- [x] 083 Enforce DPI-aware history action hit targets.
- [x] 084 Protect history action labels with ellipsis.
- [x] 085 Preserve row-selection semantics.
- [x] 086 Increase grid row height safely for high DPI.
- [x] 087 Increase grid-header height safely for high DPI.
- [x] 088 Preserve cell tooltips for truncated prompt/response text.
- [x] 089 Normalize History grid background and separators.
- [x] 090 Upgrade the history result summary to semantic status presentation.

### 091–100 — Support Diagnostics and final regression safety
- [x] 091 Detect Support Diagnostics explicitly.
- [x] 092 Normalize Support Diagnostics DPI padding.
- [x] 093 Reduce fixed title width on compact screens.
- [x] 094 Preserve the support status area as the flexible column.
- [x] 095 Keep Create Support Bundle visually primary.
- [x] 096 Enforce a DPI-aware support-bundle action size.
- [x] 097 Upgrade support generating/success/failure states semantically.
- [x] 098 Add regression coverage for all five secondary screen targets.
- [x] 099 Add regression coverage for DPI and compact-layout contracts.
- [x] 100 Add regression guards proving no persistence/runtime action is introduced by this layer.

## Scope boundary
Implementation is intentionally isolated to `src/GPTDeskTop/UI/SecondaryScreenExperience.cs`, UI regression coverage, and this receipt. It does not call monitor, Chrome/CDP, database, development-task, instance-handoff, setup or release services and does not subscribe to action-button Click events.

## Validation
Pending exact-head GitHub Actions validation.
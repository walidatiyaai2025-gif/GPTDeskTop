# UI-STABILITY-007 — Incremental Specialized UI Application

Status: **IMPLEMENTED / CI PENDING**  
Priority: **P0**  
Issue: **#193**  
Branch: `agent/ui-stability-007-idle-traversal`

## Problem
`SecondaryScreenExperienceBootstrap` discovers open forms from `Application.Idle`, but the previous `Apply(form)` path recursively traversed every descendant and reapplied all specialized presentation rules on every idle cycle. That created avoidable UI-thread work even when the application was visually unchanged.

## Delivered
- each Form receives specialized presentation once on first idle discovery
- subsequent idle discovery performs only the initialized-form guard
- Form `SizeChanged` and `DpiChanged` still reapply specialized responsive presentation
- existing controls are registered once for dynamic-child observation
- `ControlAdded` recursively registers late-created subtrees and triggers a bounded presentation refresh
- existing control-specific responsive hooks remain intact for Development Plan, Runtime Health, History and Support Diagnostics
- status presentation remains driven by its existing `TextChanged` hook
- no styling values, button-width contracts, dock ordering or responsive breakpoints were changed

## Regression coverage
`SecondaryScreenExperienceIdleRegressionTests` locks:
- one-time form presentation guard
- responsive callbacks target `ApplyPresentation` rather than the idle entry point
- recursive dynamic `ControlAdded` registration
- SizeChanged / DpiChanged / TextChanged event-driven behavior
- presentation-only architecture boundary

## Compatibility
No monitor worker, Chrome/CDP, SQLite, recovery, conversation identity, development-task scheduling/delivery or release behavior changes.

## Definition of Done
- idle rediscovery is O(open forms) initialized checks instead of repeated O(full UI tree) presentation traversal
- resize, DPI and late-control behavior remain responsive
- all existing UI-POLISH-006 and UI-STABILITY-005/006 contracts remain green
- exact final PR head passes all eight established GitHub Actions workflows
- PR merges to `main` and Issue #193 closes Completed

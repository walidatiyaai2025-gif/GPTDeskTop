# GPTDeskTop PCC Repository Constitution

PROJECT_ID: GPTDESKTOP
PROJECT_MODEL: STANDALONE
REPOSITORY: walidatiyaai2025-gif/GPTDeskTop
CONTROL_PLANE: walidatiyaai2025-gif/project-control-center
CONTROL_PLANE_VERSION: v1.6.0

## Authority

The repository is managed through the Project Control Center (PCC). Owner instructions remain final product authority, while PCC is the routing and governance authority for managed implementation work.

## Routing

GPTDeskTop is a standalone project. The canonical implementation location is the repository root (`.`). No client/product variants are currently registered.

Implementation workers must receive a PCC routing packet before product-source writes. The packet must identify `PROJECT_ID=GPTDESKTOP`, target scope `PROJECT`, canonical task identity/branch, read-first files, change boundary, validation, and handoff requirements.

The owner-authorized final-closure development lineage is `ui/premium-real-runtime-v1`. Workers must use the exact base SHA supplied by the current PCC routing packet rather than substituting default `main` or a stale branch reference.

## Existing-project safety

GPTDESKTOP remains in `POLICY_ENFORCEMENT_MODE=OBSERVE` with `WRITE_AUTHORIZED=false` unless a later durable PCC decision explicitly changes those fleet controls.

`WRITE_AUTHORIZED` is the fleet autonomous-mutation gate. In `OBSERVE`, fleet automation remains read/compare only and must not autonomously repair or mutate product repositories. `WRITE_AUTHORIZED=false` is not a blanket prohibition on owner-directed product implementation that has been routed through PCC with a canonical Task ID, resolved development lineage, routing packet, canonical task branch, explicit change boundary, required validation, and required handoff.

Owner-routed implementation workers therefore authorize their work from the current canonical PCC task/routing packet and must stay strictly inside that packet's boundary. They must not reinterpret `WRITE_AUTHORIZED=false` as permission for autonomous fleet repair, nor as a reason to ignore a valid owner-routed task.

Do not discard unique unmerged work, force-push shared implementation branches, delete branches, move tags, publish releases, or rewrite unrelated product source as part of governance synchronization.

## Product/runtime facts

GPTDeskTop is a .NET 8 WinForms application for persistent ChatGPT browser/CDP monitoring. Product implementation lives under `src/`, tests under `tests/`, and release/setup logic is part of the repository solution/workflows.

Current visible development work may be ahead of `main`; always fetch live state before routing implementation.

## Required validation

For product changes, use the repository's existing build/test/release contracts appropriate to the routed task. Exact-head provenance is required for release conclusions.

For governance-only PCC synchronization changes, validation is limited to governance consistency and must not modify product behavior.

## Completion

`CODE EXISTS != DONE`. PCC-managed work is complete only when the required task -> commit -> PR -> CI/QA -> integration/release evidence chain for that task is reconciled.

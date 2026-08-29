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

## Existing-project safety

PCC enrollment begins in `OBSERVE` mode with `WRITE_AUTHORIZED=false`. Fleet automation is read/compare only until PCC explicitly promotes the project through its write gates.

The canonical development lineage is intentionally `UNRESOLVED` at onboarding because live work exists outside default `main`; workers must fetch live branches/PRs and reconcile the actual continuation path instead of guessing from branch names.

Do not discard unique unmerged work, force-push, delete branches, move tags, publish releases, or rewrite product source as part of PCC onboarding.

## Product/runtime facts

GPTDeskTop is a .NET 8 WinForms application for persistent ChatGPT browser/CDP monitoring. Product implementation lives under `src/`, tests under `tests/`, and release/setup logic is part of the repository solution/workflows.

Current visible development work may be ahead of `main`; always fetch live state before routing implementation.

## Required validation

For product changes, use the repository's existing build/test/release contracts appropriate to the task. Exact-head provenance is required for release conclusions.

For governance-only PCC onboarding changes, validation is limited to governance consistency and must not modify product behavior.

## Completion

`CODE EXISTS != DONE`. PCC-managed work is complete only when the required task -> commit -> PR -> CI/QA -> integration/release evidence chain for that task is reconciled.

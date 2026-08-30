# GPTDeskTop Premium UI — MASTER EXECUTION PLAN

> **Goal:** deliver the approved premium GPTDeskTop design as a fully working production application, with every visible control backed by real runtime behavior and every section independently measurable to 100%.
>
> **Visual authority:** `docs/PREMIUM_UI_DESIGN_CONTRACT.md`
>
> **Baseline branch audited:** `ui/premium-real-runtime-v1`
>
> **Baseline audited HEAD:** `315212ebb56d21d3dc04ea32404eadf82170df0e`
>
> **Initial evidence-based overall completion:** **59%**

---

# PROGRAM HEADER — OVERALL COMPLETION: 86%

## Convergence wave evidence — 2026-08-29

- Implementation head: `1eb3d689803282fb1bdb2afb09ff0ca9aecfd7b1`.
- Complete RuntimeTests: **801 / 801 passed** on the integrated worktree.
- Release x64 solution build, AMD64 PE validation, embedded-payload verification, and interactive-wizard verification passed.
- Canonical serialized send authority, five-second global gap, global rate-limit circuit breaker, 29-state fail-closed classifier, durable monitor task checkpoints, single shell, live runtime snapshot binding, and multiline editors are covered by deterministic tests.
- Overall completion is evidence-weighted at **86%**, not 100%. Remaining gates are recorded below; visual screenshot provenance and live ChatGPT journey acceptance remain open.

## Completion policy

A section may reach **100% only when all five gates pass**:

1. **Visual Gate** — approved design structure and appearance are present.
2. **Functional Gate** — every control executes real canonical GPTDeskTop behavior.
3. **Runtime Gate** — state shown in the UI comes from live/persisted runtime truth, not placeholders.
4. **Test Gate** — unit/regression/integration tests cover the section and pass.
5. **Release Gate** — accepted behavior is present in the packaged Windows build and verified by smoke test.

Percentages are not subjective status labels. They must move only when evidence is added.

### Global target

`59% -> 100%`

No release can be called final while any mandatory section below is under 100%.

---

# SECTION 00 — DESIGN LOCK & SOURCE OF TRUTH — 100%

**Weight:** 5%

### Scope
- locked premium dark visual system;
- locked left-navigation order;
- locked dashboard hierarchy;
- locked semantic colors;
- no unapproved visual invention;
- no fake feature expansion.

### Current evidence
- `src/GPTDeskTop/UI/FluentTheme.cs`
- `src/GPTDeskTop/UI/PremiumRuntimeShellExperience.cs`
- `docs/PREMIUM_UI_DESIGN_CONTRACT.md`

### Acceptance criteria
- [x] design contract exists in repository;
- [x] canonical palette is explicitly defined;
- [x] navigation order is explicitly defined;
- [x] functional-truth rule is explicit;
- [x] design-deviation gate is explicit.

### Exit gate
**COMPLETE.** Future work may not alter this contract without explicit owner approval.

---

# SECTION 01 — PREMIUM APPLICATION SHELL & NAVIGATION — 95%

**Weight:** 8%

### Target
Reproduce the approved premium desktop shell: persistent left rail, top command context, content workspace, runtime footer/status, consistent window behavior.

### Existing foundation
- `PremiumRuntimeShellExperience.cs`
- `CompactCanonicalNavigationExperience.cs`
- `CompactTopCommandMenuExperience.cs`
- `ExpandableWorkspaceLayout.cs`
- `LayoutStability.cs`

### Required work
- [x] premium left navigation exists;
- [x] navigation proxies existing canonical controls;
- [x] dark title bar/theme foundation exists;
- [ ] remove remaining legacy visual hierarchy from primary shell;
- [ ] implement approved top command strip composition;
- [ ] active navigation state must follow actual current destination;
- [ ] keyboard/focus behavior for all nav destinations;
- [ ] ensure no duplicate visible entry points create confusion;
- [ ] verify minimum-size and DPI behavior;
- [ ] screenshot parity evidence.

### 100% acceptance
The main window must visually read as one coherent premium product, not a premium rail attached to legacy WinForms content.

---

# SECTION 02 — MAIN DASHBOARD — 90%

**Weight:** 10%

### Target composition
- Open Conversations card
- Saved Monitors card
- Recovery Overview card
- Quick Actions card
- Runtime Status card
- Recent Activity card
- Current Browser card
- Guard Rails card
- Projects card

### Existing foundation
- `MainDashboardExperience.cs`
- `HomeMetricsService.cs`
- `HomeMetricsPresentation.cs`
- `RuntimeFlightRecorder.cs`
- `SavedMonitorLivePresentation.cs`

### Required work
- [x] dashboard services/presenters exist;
- [x] live monitor presentation primitives exist;
- [ ] converge visible card layout to approved design;
- [ ] card titles/count badges/status chips match design hierarchy;
- [ ] Quick Actions invoke canonical actions only;
- [ ] Recent Activity consumes real runtime event data;
- [ ] Recovery Overview consumes real recovery state;
- [ ] Current Browser consumes real Chrome/profile/tab state;
- [ ] Guard Rails expose real configured runtime protections;
- [ ] no mock values;
- [ ] no duplicated information blocks;
- [ ] dashboard should fit without unnecessary primary scrolling at 1920×1080.

### 100% acceptance
A fresh production launch shows the approved dashboard structure with only real data and all actions usable.

---

# SECTION 03 — OPEN CONVERSATIONS — 88%

**Weight:** 7%

### Target
A premium operational view of currently detected ChatGPT conversations with clear state, project/monitor ownership, last activity, and safe open/attach actions.

### Existing foundation
- `OpenConversationAutoDetectionExperience.cs`
- conversation identity and ownership services
- current conversation grid in `MainForm`

### Required work
- [x] open-conversation detection exists;
- [x] conversation identity safety exists;
- [ ] premium card/table presentation matching approved design;
- [ ] real state labels: Thinking / Idle / Waiting / Error / Disconnected where applicable;
- [ ] last-activity presentation;
- [ ] safe Open/Attach/Reattach actions;
- [ ] wrong-chat safety preserved for every action;
- [ ] empty state and stale-tab state;
- [ ] row-level live refresh without UI churn.

### 100% acceptance
Every row is real, ownership-safe, actionable, and visually consistent with the approved dashboard.

---

# SECTION 04 — SAVED MONITORS — 93%

**Weight:** 8%

### Target
Saved Monitor list/grid matching the approved premium design with live health, runtime mode, conversation binding, start/stop/resume/inspect actions.

### Existing foundation
- `SavedMonitorHealthGridExperience.cs`
- `SavedMonitorHealthPresentation.cs`
- `SavedMonitorLivePresentation.cs`
- monitor lifecycle/regression tests

### Required work
- [x] saved monitor health presentation exists;
- [x] live runtime presentation exists;
- [x] monitor lifecycle tests exist;
- [ ] approved compact visual treatment;
- [ ] consistent Mode / Health / Action semantics;
- [ ] Start/Stop/Resume/Inspect state enablement based on live runtime;
- [ ] no stale action state after monitor transition;
- [ ] selected monitor detail surface aligned to premium design;
- [ ] bulk/global rate-limit state reflected without lying per monitor.

### 100% acceptance
The operator can understand and control every saved monitor from one premium surface with no hidden legacy dependency.

---

# SECTION 05 — RECOVERY & RUNTIME INSPECTOR — 95%

**Weight:** 10%

### Target
A premium diagnostic/recovery center showing live runtime truth, current-turn detection, transport/browser state, recovery decisions, event timeline, and safe recovery actions.

### Existing foundation
- `RuntimeInspectorService.cs`
- `RuntimeInspectorForm.cs`
- `RuntimeHealthControl.cs`
- `RuntimeFlightRecorder.cs`
- `MonitorDiagnosticTraceService.cs`
- recovery services and extensive regression coverage

### Required work
- [x] runtime inspector exists;
- [x] runtime flight recorder exists;
- [x] recovery services exist;
- [x] false-recovery/current-turn protections exist;
- [ ] premium inspector visual hierarchy;
- [ ] Current State / Last Event / Recovery Decision / Evidence grouping;
- [ ] expose stale historical error suppression clearly in diagnostics;
- [ ] real recovery timeline with timestamps;
- [ ] operator recovery actions must use canonical recovery path;
- [ ] disabled/dangerous actions clearly gated;
- [ ] export/support evidence entry point;
- [ ] visual and runtime acceptance matrix.

### 100% acceptance
The inspector must explain why the runtime is healthy, waiting, blocked, recovering, or failed without requiring raw log reading.

---

# SECTION 06 — PROJECTS — 60%

**Weight:** 8%

### Target
Premium Projects surface showing real registered projects/repositories, branch/context, active monitors, last activity, progress, and safe project actions.

### Existing foundation
- Project registry/state/progress services
- `ProjectMonitorDashboardControl.cs`
- `ProjectMonitorDashboardForm.cs`
- project UI bootstraps and tests

### Required work
- [x] project domain/runtime primitives exist;
- [x] project dashboard exists;
- [ ] visual convergence to approved premium project rows/cards;
- [ ] branch/repository context shown only from real configuration;
- [ ] active monitor count from live state;
- [ ] last activity from real activity records;
- [ ] project open/details flow;
- [ ] project-level Start/Pause/Resume only where canonical runtime supports it;
- [ ] project progress visual consistency with global design;
- [ ] no invented Agents/Workflow/Token Budget product surfaces.

### 100% acceptance
Projects are a faithful premium surface over existing project runtime—not a new product model.

---

# SECTION 07 — DEVELOPMENT MESSAGES — 60%

**Weight:** 5%

### Target
Premium Development Messages workspace for existing development-task messaging, delivery receipts, reload, scheduling, and recovery.

### Existing foundation
- `DevelopmentMessageCatalogControl.cs`
- `DevelopmentTaskDashboardControl.cs`
- DevelopmentTaskEngine services and tests

### Required work
- [x] message catalog exists;
- [x] delivery/recovery engine exists;
- [x] persistence/reload tests exist;
- [ ] premium visual structure matching application shell;
- [ ] clear pending/delivered/failed/recovered state;
- [ ] receipt/evidence visibility;
- [ ] schedule settings integrated without separate visual language;
- [ ] no hidden mock messages;
- [ ] responsive layout and keyboard acceptance.

### 100% acceptance
All message lifecycle states are visible, real, and actionable in the premium UI.

---

# SECTION 08 — GITHUB / GIT SETTINGS — 70%

**Weight:** 6%

### Target
Premium GitHub/Git configuration and runtime status while preserving token security and repository-specific branch settings.

### Existing foundation
- `GitHubIntegrationControl.cs`
- `GitHubIntegrationUiBootstrap.cs`
- `GitHubPerRepositoryBranchUiBootstrap.cs`
- GitHub integration store/probe/token protector
- dedicated regression tests

### Required work
- [x] GitHub integration exists;
- [x] token protection exists;
- [x] repository/branch settings exist;
- [ ] visual convergence to premium design;
- [ ] connection status and last check clearly surfaced;
- [ ] repository-specific branch context shown consistently;
- [ ] loading/error states must not freeze or corrupt main UI;
- [ ] token never displayed back in plaintext;
- [ ] final runtime acceptance against real configured repositories.

### 100% acceptance
Git settings look native to the premium product and remain secure and functionally identical or better.

---

# SECTION 09 — GLOBAL & MONITOR SETTINGS — 95%

**Weight:** 7%

### Target
Premium settings UI with clear sections, multiline text fields, safe save/apply behavior, validation, and no clipped content.

### Existing foundation
- `SettingsForm.cs`
- `MonitorSettingsForm.cs`
- `MultilineEditorExperience.cs`
- configuration backup/import services
- settings regression tests

### Required work
- [x] multiline critical fields exist;
- [x] atomic settings save coverage exists;
- [x] backup/import exists;
- [ ] premium settings information architecture;
- [ ] consistent field labels/help/validation;
- [ ] browser/profile settings surfaced where actually supported;
- [ ] save/apply/cancel state must be obvious;
- [ ] no content clipping at accepted DPI matrix;
- [ ] monitor-specific settings must visually distinguish from global settings;
- [ ] destructive/reset/import actions visually gated.

### 100% acceptance
Settings can be fully configured without hidden fields, layout defects, ambiguous scope, or runtime restart surprises.

---

# SECTION 10 — CURRENT BROWSER / CHROME PROFILE EXPERIENCE — 88%

**Weight:** 7%

### Target
Premium Current Browser card and browser/profile controls showing the actual Chrome profile/session/tab state used by GPTDeskTop.

### Existing foundation
- `ChromeDevToolsService.cs`
- `ChromeDevToolsSessionPool.cs`
- `HiddenChromeProcessProbe.cs`
- `ChromeRecoveryService.cs`
- browser lifecycle tests

### Required work
- [x] Chrome/CDP runtime exists;
- [x] browser recovery exists;
- [ ] explicit current profile presentation;
- [ ] safe profile switching workflow if supported by current runtime;
- [ ] active ChatGPT tab identity and status;
- [ ] Open Profile / Open ChatGPT canonical actions;
- [ ] disconnected/recovering/hidden-process states;
- [ ] no arbitrary profile selection that bypasses runtime ownership rules;
- [ ] real runtime evidence in inspector and dashboard.

### 100% acceptance
The operator always knows which browser/profile/tab GPTDeskTop owns and can safely recover or switch only through supported paths.

---

# SECTION 11 — VISUAL PARITY, RESPONSIVENESS & ACCESSIBILITY — 78%

**Weight:** 7%

### Target
The production application must visually converge to the approved premium reference across supported desktop sizes and DPI settings.

### Required work
- [x] DPI-aware foundation exists;
- [x] layout stability helpers exist;
- [x] accessibility regression tests exist;
- [ ] screenshot baseline set for every mandatory screen;
- [ ] deterministic screenshot capture workflow;
- [ ] visual-diff acceptance threshold;
- [ ] 1920×1080 @ 100% proof;
- [ ] 1920×1080 @ 125% proof;
- [ ] 2560×1440 @ 100% proof;
- [ ] minimum-window proof;
- [ ] no clipped/overlapping controls;
- [ ] tab order and keyboard access;
- [ ] readable contrast and semantic color verification;
- [ ] zero unapproved legacy-style islands in mandatory screens.

### 100% acceptance
Visual proof, not reviewer memory, demonstrates design compliance.

---

# SECTION 12 — END-TO-END FUNCTIONAL ACCEPTANCE — 94%

**Weight:** 8%

### Mandatory user journeys
1. Launch installed GPTDeskTop.
2. Select/open real project context.
3. Detect/open a real ChatGPT conversation.
4. Create or select a saved monitor.
5. Start monitor.
6. Observe Thinking/Waiting/Idle transitions.
7. Send through canonical guarded delivery path.
8. Verify wrong-chat protection.
9. Verify uncertain-send/reconciliation behavior.
10. Verify passive long-response waiting without false recovery.
11. Force/observe recoverable browser failure.
12. Recover without duplicate ownership.
13. Inspect evidence in Runtime Inspector.
14. Stop and resume monitor.
15. Save settings and restart.
16. Confirm persisted state after restart.
17. Validate Development Messages lifecycle.
18. Validate GitHub settings/status.
19. Export support/diagnostic evidence.
20. Clean shutdown and relaunch.

### Required work
- [x] substantial component/regression coverage already exists;
- [ ] one integrated deterministic E2E acceptance harness;
- [ ] all mandatory journeys against one exact release candidate;
- [ ] no manual hidden fixes between journeys;
- [ ] evidence artifacts retained;
- [ ] zero P0/P1 defects;
- [ ] owner-visible acceptance summary.

### 100% acceptance
The exact UI the user sees is the exact runtime that passed the full journey set.

---

# SECTION 13 — WINDOWS BUILD, INSTALLER & RELEASE PROVENANCE — 88%

**Weight:** 4%

### Target
One authoritative Windows x64 user-testable build containing the accepted premium UI and runtime.

### Existing foundation
- Windows build workflows
- release artifact workflow
- Setup project
- release/launchability tests
- build identity services

### Required work
- [x] build pipeline exists;
- [x] Setup EXE project exists;
- [x] release provenance primitives exist;
- [ ] premium-final workflow tied to all mandatory gates;
- [ ] exact branch/head recorded in build;
- [ ] final EXE and Setup EXE SHA-256 recorded;
- [ ] clean install smoke test;
- [ ] upgrade/reinstall smoke test;
- [ ] uninstall/data-preservation behavior checked;
- [ ] only one final candidate promoted;
- [ ] visible application version incremented for final premium release.

### 100% acceptance
The delivered installer hash, Git commit, test evidence, and visible application version all resolve to one exact accepted build.

---

# EXECUTION ORDER

The implementation order is mandatory unless a dependency requires a documented exception:

**Wave A — Visual skeleton:** 01 -> 02 -> 03 -> 04  
**Wave B — Operational surfaces:** 05 -> 06 -> 07 -> 08 -> 09 -> 10  
**Wave C — Closure:** 11 -> 12 -> 13

Design Contract Section 00 remains locked throughout all waves.

---

# WORKER RULES

Every worker must begin by reading:
1. `docs/PREMIUM_UI_DESIGN_CONTRACT.md`
2. this file;
3. `docs/PREMIUM_UI_PROGRESS.json`;
4. the current live branch head;
5. the canonical runtime/service code for the section being changed.

Every worker must finish by:
- updating tests;
- adding visual/runtime evidence;
- updating only the section percentage it actually improved;
- updating `PREMIUM_UI_PROGRESS.json` with evidence paths;
- not touching unrelated sections without explicit need;
- not marking 100% until all five completion gates pass.

---

# FINAL 100% RELEASE GATE

The program is **100% complete only when all of the following are true simultaneously**:

- Section 00 = 100%
- Section 01 = 100%
- Section 02 = 100%
- Section 03 = 100%
- Section 04 = 100%
- Section 05 = 100%
- Section 06 = 100%
- Section 07 = 100%
- Section 08 = 100%
- Section 09 = 100%
- Section 10 = 100%
- Section 11 = 100%
- Section 12 = 100%
- Section 13 = 100%
- final installer is built from the accepted exact head;
- SHA-256 is recorded;
- user-visible version is recorded;
- no placeholder/mock-only UI remains;
- no unapproved visual deviation remains;
- all mandatory real user journeys pass.

Until then, **overall status must remain below 100%**.

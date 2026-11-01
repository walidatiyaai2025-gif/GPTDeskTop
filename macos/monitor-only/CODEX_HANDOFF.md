# Codex handoff — GPTDeskTop Monitor Only for macOS

This is a **native SwiftUI macOS app**, not the Windows WinForms product and not a passive-only monitor. It sends an ordered sequence of user-authored messages to an authenticated ChatGPT conversation in a dedicated Google Chrome profile via CDP.

## Source of truth

- Canonical task: [GPTDeskTop #399](https://github.com/walidatiyaai2025-gif/GPTDeskTop/issues/399)
- Branch: `feature/mac-monitor-only-source-handoff`
- Scope: `macos/monitor-only/**` only; do **not** alter Windows `src/**`, `tests/**`, existing Windows build/release workflows or change the product's mainline.
- Read the repository-root `AGENTS.md` and PCC routing packet in issue #399 before writing.
- Windows reference points: `src/GPTDeskTop/Models/SimpleMonitorMessagePlan.cs`, `src/GPTDeskTop/Services/SimpleMonitorRunner.cs`, `src/GPTDeskTop/UI/SimpleMonitorForm.cs`.

## First actions for Codex on a Mac

```bash
git clone https://github.com/walidatiyaai2025-gif/GPTDeskTop.git
cd GPTDeskTop
git fetch origin feature/mac-monitor-only-source-handoff
git checkout feature/mac-monitor-only-source-handoff
cd macos/monitor-only
swiftc GPTDeskTopMonitor/Sources/MonitorModels.swift Tests/MonitorCoreTests.swift -o /tmp/monitor-model-tests
/tmp/monitor-model-tests
xcodebuild -project GPTDeskTopMonitor.xcodeproj -scheme 'GPTDeskTop Monitor Only' -configuration Debug -destination 'platform=macOS' -derivedDataPath build CODE_SIGNING_ALLOWED=NO build
```

Then run the scheme in Xcode, connect the app to dedicated Chrome, sign in to ChatGPT, and perform an end-to-end **controlled** delivery test with short, non-destructive messages.

## Required Codex acceptance

1. Fix all Xcode build errors/warnings that affect app correctness. Verify macOS 13+ Intel and Apple Silicon compatibility where runners are available.
2. Import a Windows-compatible v1 JSON plan. Verify ordering, enabled flags, per-message delay, Run Once, Loop, and persistence.
3. Validate exactly-once-safety: durable pre-send marker, read-only receipt reconciliation, **no automatic retry after uncertain send**, no new send while assistant is generating, and wait for a completed response before proceeding.
4. Verify Stop, re-launch/resume of pending messages, and runtime inspector status under Chrome restarts, CDP errors, authentication expiry, 429/rate limits, and ChatGPT UI changes. Never silently bypass restrictions.
5. Validate `build-on-mac.command` outputs an unsigned `.app` ZIP. Consider a signed/notarized DMG only once credentials and owner approval exist; do not invent certificates.
6. Add real macOS tests and attach exact-head CI evidence. Keep this PR unmerged until its checks are green and QA confirms live CDP behavior.

## Known limits

This source drop is a **prototype**; source-level model tests and Swift parsing have been checked outside macOS, but the full Xcode build and live Chrome/CDP runtime have **not** been verified yet. Do not announce a release-ready .app, DMG or deployed automation without evidence.

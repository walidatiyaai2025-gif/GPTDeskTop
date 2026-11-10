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
swiftc GPTDeskTopMonitor/Sources/MonitorModels.swift GPTDeskTopMonitor/Sources/MonitorStore.swift Tests/MonitorCoreTests.swift -o /tmp/monitor-model-tests
/tmp/monitor-model-tests
xcodebuild -project GPTDeskTopMonitor.xcodeproj -scheme 'GPTDeskTop Monitor Only' -configuration Debug -destination 'platform=macOS' -derivedDataPath build CODE_SIGNING_ALLOWED=NO build
```

Then run the scheme in Xcode. The app attaches only to the Chrome profile already open on the Mac. The operator must enable **Allow remote debugging for this browser instance** at `chrome://inspect/#remote-debugging` and approve the app if prompted. If Chrome has no live endpoint, the app reports the issue and stops; it must never launch another profile or window. The app creates a new ChatGPT tab inside the current profile and sends only the user-authored plan there.

Perform an end-to-end **controlled** delivery test with short, non-destructive messages.

## Required Codex acceptance

1. Fix all Xcode build errors/warnings that affect app correctness. Verify macOS 13+ Intel and Apple Silicon compatibility where runners are available.
2. Import a Windows-compatible v1 JSON plan. Verify ordering, enabled flags, per-message delay, Run Once, Loop, and persistence.
3. Validate exactly-once-safety: durable pre-send marker, read-only receipt reconciliation, **no automatic retry after uncertain send**, no new send while assistant is generating, and wait for a completed response before proceeding.
4. Verify Stop, re-launch/resume of pending messages, and runtime inspector status under Chrome restarts, CDP errors, authentication expiry, 429/rate limits, and ChatGPT UI changes. Never silently bypass restrictions.
5. Validate `build-on-mac.command` outputs an unsigned `.app` ZIP. Consider a signed/notarized DMG only once credentials and owner approval exist; do not invent certificates.
6. Add real macOS tests and attach exact-head CI evidence. Keep this PR unmerged until its checks are green and QA confirms live CDP behavior.

## Known limits

Live ordered send, response completion, 30-second delay, Loop, Run Once, and restart reconciliation without duplicate delivery have passed on macOS 15.8.1 / Xcode 26.3 with Chrome's existing signed-in profile and CDP enabled. The test `.app` ZIP is unsigned and unnotarized. This remains a prototype; do not announce a signed/notarized release or deployed automation.

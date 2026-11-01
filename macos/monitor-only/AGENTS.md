# macOS Monitor Only — Codex workspace

Follow the repository root `AGENTS.md` and PCC routing packet in issue #399. Canonical task: `GPTDESKTOP-MAC-001-MONITOR-ONLY-SOURCE-HANDOFF`.

Scope is **only** `macos/monitor-only/**` and its dedicated macOS CI job. Never modify the WinForms application or integrate this work into Windows releases. This is an independent macOS SwiftUI prototype.

Prioritize native macOS Xcode compilation, deterministic message-plan tests, Chrome/CDP safety, durable exact-send checkpoints and real-machine acceptance before merging. Never claim physical ChatGPT send or signed DMG success from source parse alone. If a CDP send is ambiguous, stop instead of re-sending.

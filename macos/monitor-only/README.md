# GPTDeskTop Monitor Only — macOS native prototype (0.1.0)

Independent **SwiftUI macOS** implementation of the **Monitor Only** part of the Windows GPTDeskTop project (not a port of all WinForms features). Reference repository: https://github.com/walidatiyaai2025-gif/GPTDeskTop .

## Included

- Ordered message sequence with Add / Remove / Move Up / Move Down / label / editor / enabled flags.
- Import and export JSON compatible with the Windows `SimpleMonitorMessagePlan` (`schemaVersion=1`, `name`, `loop`, `defaultDelaySeconds`, `messages[{label,text,enabled,delaySeconds,sent}]`). Imported sent flags are intentionally reset; local runtime checkpoints are authoritative.
- Run Once and Loop, default/per-message **30–3600s** post-response delays.
- Controlled Chrome via **localhost Chrome DevTools Protocol**. The app first uses the existing Chrome profile when Chrome's **Allow remote debugging** option is enabled; otherwise it launches an isolated profile under `~/Library/Application Support/GPTDeskTopMonitorMac/ChromeProfile`.
- First Start creates a fresh ChatGPT tab. Subsequent steps stay in that conversation. The same saved CDP tab is required for resume.
- Physical-send safety marker persisted *before* click; no automated resend after ambiguous delivery.
- Read-only user-message receipt verification; confirmation is checkpointed atomically before waiting for assistant response.
- Response completion gate, operator Stop, conservative blocking of unclear outcomes, basic live Runtime Inspector log.

## Requirements and build on a Mac

- **macOS 13+**, Xcode 15+ (Xcode 26 is suitable), Google Chrome installed in `/Applications`.
- Open **`GPTDeskTopMonitor.xcodeproj`** in Xcode, select scheme **GPTDeskTop Monitor Only**, destination **My Mac**, press **Run**.
- Or double-click **`build-on-mac.command`**. The compiled .app will be under `build/Build/Products/Release` and its ZIP under `dist/`.
- To use an existing signed-in Chrome profile, open `chrome://inspect/#remote-debugging`, enable **Allow remote debugging for this browser instance**, and approve the connection prompt if Chrome shows one. This gives CDP access to the active browser session, including its tabs, cookies, and site data; only enable it for trusted local apps. The app creates a new ChatGPT tab for a plan and resumes only its saved tab.
- Click **Connect / Open Chrome** and **Start Monitor** only after choosing the intended profile and editing/importing the message sequence.
- For Run Once: confirmed sends are marked `sent=true` in local `monitor-state.json`. On restart the app resumes pending messages, without blindly resending an ambiguous attempt.

## Key safety decisions

The client uses the website, not an official ChatGPT automation API. Site DOM changes, rate limits, login challenges and network interruptions can prevent execution. Nothing bypasses usage quotas. If there is an uncertain send, **the app blocks** and requires the user to inspect the exact browser conversation. **Review / Clear Hold** can cause duplication if the user ignores that warning. No unattended release or production claims.

This first macOS prototype does **not** replicate the full Windows implementation: no selectable Windows/Chrome profile catalog, no automatic tab recovery/rollover, no background agent, no system notifications, no cross-device state sync, no notarized DMG. It does implement the essential message queue and safety checkpoint behavior.

## Verification performed here

- On macOS 15.8.1 / Xcode 26.3, `ci/verify-mac.sh` compiled and ran model, delay, loop and restart checkpoint tests and built the universal Debug app — **PASS**.
- `build-on-mac.command` built the universal Release `.app` and unsigned ZIP — **PASS**.
- Live Chrome/CDP acceptance on the existing signed-in profile — **PASS**: sent two messages in order and verified each receipt and completed reply; confirmed the 30-second post-response delay; Run Once completed; restart reconciled a previously delivered message without sending a duplicate; Loop sent its first item again after the reply and 30-second delay, then stopped with its checkpoint retained.
- Release ZIP is a test build and remains unsigned and unnotarized.

## Local core model test (on Mac)

```sh
swiftc GPTDeskTopMonitor/Sources/MonitorModels.swift GPTDeskTopMonitor/Sources/MonitorStore.swift Tests/MonitorCoreTests.swift -o /tmp/monitor-model-tests
/tmp/monitor-model-tests
```

No changes have been pushed to the Windows repository. Its `AGENTS.md` requires PCC routing before product-source writes; this is a separate distributable source prototype.

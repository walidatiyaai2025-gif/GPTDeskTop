# UI-BRAND-001 — Branded wide Saved Monitors + stable first-message verification

## Scope
- Use the supplied GPTDeskTop artwork as the Windows application/setup icon and tray/window icon.
- Make Saved Monitors the dominant main workspace at about 72%, roughly 2.6x the Open Conversations pane.
- Hide Auto Reply from the Saved Monitors dashboard grid and quick selected-monitor card while retaining it in monitor settings/runtime configuration.
- Fix New Chat + Monitor false-negative first-message verification when ChatGPT navigates from the new-chat shell to a stable `/c/{conversation-id}` target.

## Recovery invariant
A bootstrap message is never resent merely because the original target navigated. GPTDeskTop refreshes the CDP target binding/WebSocket metadata and then verifies the already-delivered message on the stable conversation using `requireNewTurn: false`. A monitor is created only after verified delivery and a stable conversation identity.

## Acceptance
- App and Setup compile with `Assets/GPTDeskTop.ico`.
- Main and tray surfaces use the branded executable icon.
- Saved Monitors starts around 72% width and Auto Reply is absent from the dashboard surface.
- Transient stale-CDP verification rebinds target metadata and can validate the existing bootstrap message without duplication.
- Runtime regression tests and all stable PR gates are green before merge.

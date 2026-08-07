# GPTDeskTop

.NET 8 WinForms desktop monitor for a selected Chrome/ChatGPT tab.

## What this version monitors

The first version used a simulated inbound message source. This version monitors the **actual ChatGPT web page opened in Chrome** through the Chrome DevTools Protocol (CDP).

The UI shows a live list of Chrome page targets with:

- Tab ID
- Title
- URL

Select a ChatGPT tab, type an automatic reply such as `كمل`, and press **Start Monitoring**. When a new ChatGPT assistant response finishes, GPTDeskTop detects it and sends the configured text into that same ChatGPT conversation.

## Chrome requirement

A normal Chrome instance does not expose its tab IDs/DOM to an external desktop process. Use the application's **Launch Monitor Chrome** button. It starts a dedicated Chrome profile with:

```text
--remote-debugging-port=9222
--user-data-dir=%LOCALAPPDATA%\GPTDeskTop\ChromeProfile
```

This is intentional: modern Chrome restricts remote debugging against the normal/default profile.

Sign into ChatGPT once in that dedicated Chrome window. The dedicated profile is persistent between launches.

## Workflow

1. Run GPTDeskTop.
2. Click **Launch Monitor Chrome**.
3. Open/sign in to ChatGPT in that Chrome window.
4. Open the conversation to monitor.
5. Click **Refresh Chrome Tabs**.
6. Select the row whose URL is `chatgpt.com`.
7. Enter the automatic reply text, e.g. `كمل`.
8. Click **Start Monitoring**.
9. Each completed new assistant reply triggers one automatic reply.
10. Click **Stop Monitoring** to stop the continuation loop.

> Note: if your auto reply is `كمل`, each new assistant answer can trigger another `كمل`, so the conversation can continue repeatedly until you stop monitoring.

## Build and run

Requirements: Windows 10/11 and .NET 8 SDK.

```powershell
git clone https://github.com/walidatiyaai2025-gif/GPTDeskTop.git
cd GPTDeskTop
dotnet restore .\GPTDeskTop.sln
dotnet run --project .\src\GPTDeskTop\GPTDeskTop.csproj
```

No OpenAI API key is required for this browser-monitoring mode because GPTDeskTop is controlling the user's already-authenticated ChatGPT web session rather than calling the OpenAI API directly.

## Local database

`appdata.db` is automatically created beside the executable. It contains:

- `MessageLogs`: ID, timestamp, direction, prompt, response, status.
- `AppSettings`: key/value settings.

Detected ChatGPT replies are logged as `Inbound`; automatic messages sent to ChatGPT are logged as `Outbound`.

## Main source files

- `Services/ChromeDevToolsService.cs` — starts monitor Chrome, reads tabs, evaluates DOM state, sends messages.
- `Services/ChatGptMonitorService.cs` — polling, stable-response detection, cancellation and auto-reply loop.
- `UI/MainForm.cs` — Chrome tabs grid, Tab ID/Title/URL, auto-reply textbox, Start/Stop, live activity and history.
- `Data/LocalDatabase.cs` — SQLite initialization and history.
- `Configuration/AppConfig.cs` — CDP, polling and database settings.

## Robustness

ChatGPT is a web application and its DOM can change. The implementation uses multiple fallback selectors for the prompt editor and send/stop buttons. If OpenAI changes the page markup significantly, update those selectors in `ChromeDevToolsService.cs`.

# GPTDeskTop

.NET 8 WinForms persistent multi-tab monitor for ChatGPT pages opened in a dedicated Chrome/CDP session.

Current application version: **1.6.0**.

## Solution structure

`GPTDeskTop.sln` contains exactly three SDK-style projects supported directly by Visual Studio 2022:

1. `GPTDeskTop` - main .NET 8 WinForms application.
2. `GPTDeskTop.Publish` - produces a `win-x64` self-contained single-file payload under `Output\Publish`.
3. `GPTDeskTop.Setup` - embeds the payload and produces `Output\Setup\GPTDeskTop-Setup.exe`.

## Build Setup from Visual Studio

Select `Release | x64`, then **Build > Build Solution**. Final installer:

```text
Output\Setup\GPTDeskTop-Setup.exe
```

## Monitoring and settings

Every saved monitor keeps its own Auto Reply, Reply Delay (`0-300` seconds), Monitor Timer (`1-60` seconds), Enabled state, Tab ID, title and URL.

Global Settings stored in SQLite include:

- Default Auto Reply
- Default Monitor Delay
- Default Monitor Timer
- No-response refresh timeout in seconds (default `180`, i.e. 3 minutes)
- Timeout Recovery Message
- Balloon Duration
- Balloon Sound Enabled / Sound Type

If a running monitor receives no new assistant response for `NoResponseRefreshSeconds`, GPTDeskTop refreshes only that tab, records `NoResponseRefresh` in history, resets the watchdog, and continues monitoring.

## Exception diagnostics

GPTDeskTop now captures application/UI/task/monitor exceptions. Full exception text and stack trace are stored in two places:

- **Stored History** inside the application with status `Exception`.
- `logs\exceptions-YYYYMMDD.log` beside the application executable.

A startup failure also falls back to `startup-error.log` if the database cannot be initialized.

## Monitor status indicator

The Saved Monitors Status column displays:

- `🟢 Running` while that monitor worker is active.
- `🔴 Stopped` when it is not running.

## Message delivery timeout recovery

For `Message delivery timed out. Please try again.` GPTDeskTop saves the error first, opens a fresh ChatGPT tab, sends the configured recovery message (default `كمل`), moves the same Monitor ID to the new tab, continues monitoring and closes the old timed-out tab.

Other ChatGPT errors use single-tab refresh recovery.

## Chrome lifecycle

**Hide Chrome** now uses Chrome DevTools window minimization for all dedicated monitor windows before native-window fallback. This keeps CDP/JavaScript execution alive while hiding/minimizing the monitor Chrome session. **Show Chrome** restores those windows.

When GPTDeskTop closes, the monitor workers stop and dedicated monitor tabs are closed.

## Fluent UI and context menus

The WinForms UI uses a Fluent/WinUI-inspired visual system. Open Tabs, Saved Monitors and History grids expose right-click actions for their relevant add/start/stop/edit/delete/refresh operations.

## Developer run

```powershell
git pull origin main
dotnet restore .\GPTDeskTop.sln
dotnet run --project .\src\GPTDeskTop\GPTDeskTop.csproj
```

No OpenAI API key is required in browser-monitoring mode.

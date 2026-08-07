# GPTDeskTop

.NET 8 WinForms persistent multi-tab monitor for ChatGPT pages opened in a dedicated Chrome/CDP session.

Current application version: **1.7.0**.

## Solution structure

`GPTDeskTop.sln` contains exactly three SDK-style projects supported directly by Visual Studio 2022:

1. `GPTDeskTop` - main .NET 8 WinForms application.
2. `GPTDeskTop.Publish` - produces a `win-x64` self-contained single-file payload under `Output\Publish`.
3. `GPTDeskTop.Setup` - embeds that payload and produces `Output\Setup\GPTDeskTop-Setup.exe`.

## Build Setup from Visual Studio

Select `Release | x64`, then **Build > Build Solution**. Final installer:

```text
Output\Setup\GPTDeskTop-Setup.exe
```

## Monitoring and settings

Every saved monitor keeps its own Auto Reply, Reply Delay (`0-300` seconds), Monitor Timer (`1-60` seconds), Enabled state, Tab ID, title and URL.

Global Settings stored in SQLite include Default Auto Reply, Default Monitor Delay, Default Monitor Timer, No-response refresh timeout (default `180` seconds), Timeout Recovery Message, Balloon Duration and Balloon Sound settings.

If a running monitor receives no new assistant response for `NoResponseRefreshSeconds`, GPTDeskTop refreshes only that tab, records `NoResponseRefresh` in history, resets the watchdog and continues monitoring.

## Chrome DevTools resilience

ChatGPT can rebuild its page context while a DevTools evaluation is in progress, producing:

```text
Chrome DevTools error: {"code":-32000,"message":"Promise was collected"}
```

Version 1.7.0 avoids async JavaScript promises for normal DOM reads and message sending. DOM reads and send steps are synchronous CDP evaluations, with a C# delay between editor input and Send click. A transient `Promise was collected` is retried internally up to three times before it is treated as a real failure.

## Crash detection and recovery

GPTDeskTop stores `LastShutdownClean`, `CrashCount` and `CrashRecoveryPending` in SQLite.

On a normal exit, `LastShutdownClean=1`. If the next startup sees the previous process was not closed cleanly, it increments Crash Count and automatically:

1. Stops any recovered worker state.
2. Closes leftover dedicated Monitor Chrome tabs.
3. Reopens every saved monitor URL.
4. Sends the configured recovery message (default `كمل`) to every recovered tab, retrying while ChatGPT loads.
5. Updates the saved monitor with the new Chrome Tab ID.
6. Restarts every enabled monitor.

If a fatal exception escapes the application, GPTDeskTop attempts one automatic restart. A 30-second restart guard prevents a crash/restart loop.

## Home dashboard

The home toolbar includes two live cards:

- **Monitors** — running monitors / total saved monitors.
- **Crashes** — persistent unclean-shutdown count.

The Saved Monitors Status column is formatted as a real visual lamp:

- green `● Running`
- red `● Stopped`

## Exception diagnostics

Application/UI/task/monitor exceptions are stored in both:

- **Stored History** inside GPTDeskTop.
- `logs\exceptions-YYYYMMDD.log` with full stack trace.

A startup failure also falls back to `startup-error.log` if the database cannot be initialized.

## Message delivery timeout recovery

For `Message delivery timed out. Please try again.` GPTDeskTop saves the error first, opens a fresh ChatGPT tab, sends the configured recovery message, moves the same Monitor ID to the new tab, continues monitoring and closes the old timed-out tab.

Other ChatGPT errors use single-tab refresh recovery.

## Chrome lifecycle

**Hide Chrome** minimizes the dedicated monitor windows through CDP and also applies the native hide operation when the Chrome window handle is available. Monitoring/CDP remains active. **Show Chrome** restores the windows.

When GPTDeskTop closes normally, monitor workers stop and dedicated monitor tabs are closed.

## Developer run

```powershell
git pull origin main
dotnet restore .\GPTDeskTop.sln
dotnet run --project .\src\GPTDeskTop\GPTDeskTop.csproj
```

No OpenAI API key is required in browser-monitoring mode.

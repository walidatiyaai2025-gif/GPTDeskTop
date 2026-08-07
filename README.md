# GPTDeskTop

.NET 8 WinForms persistent multi-tab monitor for ChatGPT pages opened in a dedicated Chrome/CDP session.

Current application version: **1.5.0**.

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

The target PC does not need a separate .NET 8 runtime installation.

## Multi-tab monitoring

Every saved monitor persists its own Auto Reply, Reply Delay (`0-300` seconds), Monitor Timer (`1-60` seconds), Enabled state, Tab ID, title and URL. Selecting multiple open tabs opens a separate Monitor Settings dialog for each tab.

Default values for newly added monitors are configured in **Settings** and stored in SQLite:

- Default Auto Reply
- Default Monitor Delay
- Default Monitor Timer
- Timeout Recovery Message
- Balloon Duration
- Balloon Sound Enabled
- Balloon Sound Type

## Message delivery timeout recovery

The ChatGPT page state explicitly detects errors such as:

```text
Message delivery timed out. Please try again.
```

When this condition is detected:

1. The exact error text is saved to `MessageLogs` first.
2. GPTDeskTop opens a new ChatGPT chat tab.
3. It sends the configured Timeout Recovery Message (default `كمل`).
4. The existing Saved Monitor record is moved to the new tab, preserving the same Monitor ID, per-tab Delay and Timer.
5. The new tab immediately continues under the existing background monitor worker.
6. The old timed-out tab is closed.
7. Recovery and outbound message operations are stored in history.

Other ChatGPT errors continue to use the normal single-tab refresh recovery flow.

## Chrome lifecycle

**Hide Chrome** hides the dedicated monitor Chrome while background monitoring continues. **Show Chrome** restores it.

When GPTDeskTop itself closes, all tabs in the dedicated Monitor Chrome session are closed automatically after monitor workers stop.

## Fluent UI and context menus

The WinForms UI uses a Fluent/WinUI-inspired visual system: Segoe UI Variable typography, flat surfaces, accent actions, modern grid headers/selection and consistent primary/danger buttons.

All grids expose right-click context menus:

- Open Tabs: Add selected tab(s), Refresh, Close selected tab.
- Saved Monitors: Start, Stop, Edit Settings, Delete Monitor, Add selected open tab.
- History: Refresh, Delete selected log, Clear all history.

## Notifications

Every completed ChatGPT response produces a Windows taskbar/tray balloon and is persisted in SQLite. Notification duration and application notification sound can be configured from Settings. Error replies use an error-style balloon and alert sound.

## Local database

`appdata.db` is automatically created beside the executable and upgraded in place.

- `SavedMonitors`: tab identity, Auto Reply, per-tab Delay/Timer and Enabled state.
- `AppSettings`: defaults, notification settings, recovery message and Chrome preferences.
- `MessageLogs`: inbound/outbound/system history with MonitorId, TabId and TabTitle.

## Developer run

```powershell
git pull origin main
dotnet restore .\GPTDeskTop.sln
dotnet run --project .\src\GPTDeskTop\GPTDeskTop.csproj
```

No OpenAI API key is required in browser-monitoring mode.

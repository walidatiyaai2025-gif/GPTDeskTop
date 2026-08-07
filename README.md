# GPTDeskTop

.NET 8 WinForms persistent multi-tab monitor for ChatGPT pages opened in a dedicated Chrome/CDP session.

## Multi-tab workflow

GPTDeskTop now supports any number of independent saved monitors.

1. Click **Launch Monitor Chrome**.
2. Open/sign in to ChatGPT in that Chrome window.
3. Open every conversation you want to monitor.
4. Click **Refresh Chrome Tabs**.
5. Select an open tab.
6. Enter that tab's automatic reply, for example `كمل`.
7. Click **Add Selected Tab**.
8. Repeat for any additional tabs. Every saved monitor can have a different reply.
9. Use **Start Selected** for one monitor, or **Start All Enabled** for all enabled monitors.
10. Use **Stop Selected** or **Stop All** independently.

## Saved Monitor controls

Each saved monitor persists:

- Monitor database ID
- current Chrome Tab ID
- tab title
- exact conversation URL
- individual Auto Reply text
- Enabled state
- created/updated timestamps

Use **Save Monitor** after editing Auto Reply or Enabled. Use **Delete Monitor** to remove only the saved monitor definition; it does not close the Chrome tab and it does not erase historical logs.

If Chrome later assigns a different Tab ID, GPTDeskTop attempts to reconnect the saved monitor by its exact stored conversation URL.

## Concurrent monitoring

`ChatGptMonitorService` runs one cancellable background worker for each Monitor ID. Every worker maintains independent ChatGPT response-stability/de-duplication state, so activity in one tab does not control or reset another tab.

A reply such as `كمل` can create a continuing response loop for that monitor until it is stopped. Different tabs can run different loops simultaneously.

## Chrome requirement

Use **Launch Monitor Chrome**. It starts a persistent dedicated profile with Chrome remote debugging enabled:

```text
--remote-debugging-port=9222
--user-data-dir=%LOCALAPPDATA%\GPTDeskTop\ChromeProfile
```

Sign in to ChatGPT once in this dedicated Chrome profile. The profile is reused between launches.

## Local database

`appdata.db` is automatically created beside the executable and upgraded in place.

Tables:

- `SavedMonitors`: all selected monitor tabs and their individual configuration.
- `AppSettings`: key/value application settings such as the default auto reply.
- `MessageLogs`: inbound/outbound activity including MonitorId, TabId and TabTitle.

Existing databases from the earlier version are upgraded by adding the new monitor-aware log columns; the database is not reset.

History supports:

- Refresh History
- Delete Selected Log
- Clear History

## Build and run

Requirements: Windows 10/11 and .NET 8 SDK.

```powershell
git clone https://github.com/walidatiyaai2025-gif/GPTDeskTop.git
cd GPTDeskTop
dotnet restore .\GPTDeskTop.sln
dotnet run --project .\src\GPTDeskTop\GPTDeskTop.csproj
```

For an existing clone:

```powershell
git pull origin main
dotnet restore .\GPTDeskTop.sln
dotnet run --project .\src\GPTDeskTop\GPTDeskTop.csproj
```

No OpenAI API key is required in browser-monitoring mode because GPTDeskTop interacts with the authenticated ChatGPT web session through Chrome DevTools Protocol rather than calling the OpenAI API directly.

## Main source files

- `Services/ChromeDevToolsService.cs` — Chrome startup, tab discovery, ChatGPT DOM state and message send.
- `Services/ChatGptMonitorService.cs` — independent concurrent workers per saved monitor.
- `UI/MainForm.cs` — open tabs, saved monitor CRUD, per-monitor controls, activity and history.
- `Data/LocalDatabase.cs` — SQLite initialization, schema upgrades, monitor/settings/history CRUD.
- `Models/Models.cs` — Chrome tabs, saved monitor and log models.
- `TaskPlanner.md` — team implementation/status plan.

## Validation checklist

- Add two or more different ChatGPT tabs with different automatic replies.
- Start both and confirm every response is sent only to its own tab.
- Stop one monitor and confirm the others continue running.
- Restart GPTDeskTop and verify Saved Monitors and Auto Reply values return from SQLite.
- Restart Chrome, reopen the same conversation URL, refresh tabs and verify URL fallback can resolve the monitor.
- Delete a monitor and verify the Chrome tab remains open.
- Verify monitor-aware history shows which monitor produced every inbound/outbound row.

## Robustness note

ChatGPT is a web application and its DOM can change. The browser integration uses fallback selectors, but a major ChatGPT markup change may require updating selectors in `ChromeDevToolsService.cs`.

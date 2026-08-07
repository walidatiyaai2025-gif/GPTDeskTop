# GPTDeskTop

.NET 8 WinForms persistent multi-tab monitor for ChatGPT pages opened in a dedicated Chrome/CDP session.

Current application version: **1.4.0**.

## Solution structure

`GPTDeskTop.sln` now contains exactly three SDK-style projects that Visual Studio 2022 can open without WiX support:

1. `GPTDeskTop` - the main .NET 8 WinForms application.
2. `GPTDeskTop.Publish` - builds a `win-x64` self-contained, single-file application payload under `Output\Publish`.
3. `GPTDeskTop.Setup` - embeds that payload and produces the final standalone installer `Output\Setup\GPTDeskTop-Setup.exe`.

The previous WiX `.wixproj` projects were removed because they appeared as Unsupported in Visual Studio environments without the WiX extension.

## Build Setup from Visual Studio

1. Pull the latest `main` branch.
2. Open `GPTDeskTop.sln` in Visual Studio 2022.
3. Select `Release | x64`.
4. Choose **Build > Build Solution**.
5. Project dependencies build in this order:
   - `GPTDeskTop`
   - `GPTDeskTop.Publish`
   - `GPTDeskTop.Setup`
6. Final installer:

```text
Output\Setup\GPTDeskTop-Setup.exe
```

Published application payload:

```text
Output\Publish\GPTDeskTop.exe
Output\Publish\appsettings.json
Output\Publish\Version.txt
Output\Publish\ReleaseNotes.txt
```

Both the application payload and final Setup EXE are `win-x64` self-contained. The target Windows PC does not need a separate .NET 8 runtime installation.

## Setup behavior

The installer installs GPTDeskTop for the current Windows user under:

```text
%LOCALAPPDATA%\Programs\GPTDeskTop
```

It creates Desktop and Start Menu shortcuts, registers GPTDeskTop in Windows Apps/Uninstall information, and copies an uninstall-capable setup executable into the install directory. Existing `appdata.db` data is not overwritten by upgrades and is preserved during uninstall.

## Multi-tab workflow

GPTDeskTop supports any number of independent saved monitors.

1. Click **Launch Monitor Chrome**.
2. Open/sign in to ChatGPT and open every conversation you want to monitor.
3. Click **Refresh Chrome Tabs**.
4. Select one or more open tabs with Ctrl/Shift.
5. Click **Add Selected Tab(s)**.
6. A **Monitor Settings** dialog opens separately for every selected tab.
7. Configure that tab's Auto Reply, Delay Before Send, Monitor Timer and Enabled state.
8. Use **Start Selected** or **Start All Enabled**.

Every monitor persists its own Auto Reply, Reply Delay (`0-300` seconds), Monitor Timer (`1-60` seconds), Enabled state, Tab ID, title and exact URL.

## Notifications, recovery and Chrome visibility

Every completed ChatGPT reply is saved to SQLite and shown as a Windows tray balloon. Error responses are saved first, then only the affected Chrome tab is refreshed. **Hide Chrome** hides the dedicated monitor Chrome window while monitoring continues; **Show Chrome** restores it.

## Local database

`appdata.db` is automatically created beside the installed executable and upgraded in place.

- `SavedMonitors`: tab identity, Auto Reply, per-tab ReplyDelaySeconds, per-tab TimerSeconds and Enabled state.
- `AppSettings`: global application settings.
- `MessageLogs`: inbound/outbound/system history with MonitorId, TabId and TabTitle.

## Developer run

```powershell
git pull origin main
dotnet restore .\GPTDeskTop.sln
dotnet run --project .\src\GPTDeskTop\GPTDeskTop.csproj
```

No OpenAI API key is required in browser-monitoring mode.

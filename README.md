# GPTDeskTop

.NET 8 WinForms persistent multi-tab monitor for ChatGPT pages opened in a dedicated Chrome/CDP session.

Current application version: **1.3.0**.

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

Every monitor persists its own:

- Auto Reply text
- Reply Delay in seconds (`0-300`)
- Monitor Timer in seconds (`1-60`), which controls how often that tab is checked
- Enabled state
- Tab ID, title and exact URL

Double-click a saved monitor or click **Monitor Settings** to edit its Delay/Timer later. Stop a running monitor before changing its runtime timing settings.

## Notifications and recovery

Every completed ChatGPT reply is saved to SQLite and shown as a Windows tray balloon. Error responses are saved first, then only the affected Chrome tab is refreshed. Balloon duration is configurable from the tray Settings dialog.

## Chrome visibility

**Hide Chrome** hides the dedicated monitor Chrome window while monitoring continues through CDP. **Show Chrome** restores it. The preference is saved in SQLite.

## Local database

`appdata.db` is automatically created beside the executable and upgraded in place.

Tables:

- `SavedMonitors`: tab identity, Auto Reply, per-tab ReplyDelaySeconds, per-tab TimerSeconds and Enabled state.
- `AppSettings`: global settings such as balloon duration, default delay and Chrome visibility.
- `MessageLogs`: inbound/outbound/system activity including MonitorId, TabId and TabTitle.

## Build and run

Requirements: Windows 10/11 and .NET 8 SDK.

```powershell
git pull origin main
dotnet restore .\GPTDeskTop.sln
dotnet run --project .\src\GPTDeskTop\GPTDeskTop.csproj
```

No OpenAI API key is required in browser-monitoring mode.

## Build Setup from Visual Studio

The solution now contains three projects:

- `GPTDeskTop` - application
- `GPTDeskTop.Setup` - WiX MSI package
- `GPTDeskTop.Bootstrapper` - final Setup EXE

For Visual Studio 2022, install the **HeatWave for VS2022 / WiX Toolset** extension so `.wixproj` projects load in Solution Explorer. NuGet restores WiX Toolset 5 packages automatically.

Then:

1. Open `GPTDeskTop.sln`.
2. Select **Release | x64**.
3. Build **GPTDeskTop.Bootstrapper** or **Build Solution**.
4. The MSI is generated under `src\GPTDeskTop.Setup\bin\Release\`.
5. The final installer is generated under `src\GPTDeskTop.Bootstrapper\bin\Release\` as **GPTDeskTop-Setup.exe**.

The setup build publishes the application as **win-x64 self-contained**, so the target PC does not need a separate .NET 8 runtime installation.

## Main source files

- `Services/ChromeDevToolsService.cs` - Chrome/CDP integration.
- `Services/ChatGptMonitorService.cs` - independent monitor workers and per-tab timers/delays.
- `UI/MainForm.cs` - home UI and monitor management.
- `UI/MonitorSettingsForm.cs` - per-tab add/edit settings.
- `Data/LocalDatabase.cs` - SQLite schema and CRUD.
- `src/GPTDeskTop.Setup/*` - MSI project.
- `src/GPTDeskTop.Bootstrapper/*` - Setup EXE project.
- `TaskPlanner.md` - team implementation/status plan.

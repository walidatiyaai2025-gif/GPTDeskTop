# GPTDeskTop

A .NET 8 Windows Forms desktop chat monitor and OpenAI auto-responder designed for repository `walidatiyaai2025-gif/GPTDeskTop`.

## Features

- Start/stop background monitoring without freezing the UI.
- `PeriodicTimer` + `CancellationTokenSource` for low-overhead polling.
- OpenAI Chat Completions REST integration using `HttpClient`.
- Local SQLite database (`appdata.db`) using EF Core 8.
- Automatic local database/table creation on startup.
- Inbound/outbound audit records with timestamp, prompt, response, and status.
- Timestamped live activity log.
- Newest-first message history DataGridView.
- Simulated message source for immediate end-to-end testing.
- Replaceable `IMessageSource` abstraction for a real upstream chat system.

## Requirements

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 17.8+ recommended with .NET desktop development workload
- OpenAI API key with access to the configured model

## Setup

1. Clone/open the repository.
2. Set your OpenAI API key as an environment variable (recommended):

   PowerShell:

   ```powershell
   $env:OPENAI_API_KEY = "your_api_key_here"
   ```

   To persist for the current Windows user:

   ```powershell
   [Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "your_api_key_here", "User")
   ```

3. Restore and run:

   ```powershell
   dotnet restore .\GPTDeskTop.sln
   dotnet run --project .\src\GPTDeskTop\GPTDeskTop.csproj
   ```

4. Click **Start Monitoring**. With simulation enabled, an inbound message is periodically generated and sent to OpenAI.
5. Click **Stop Monitoring** to cancel gracefully.
6. Click **Refresh History** to reload persisted rows from SQLite.

## Configuration

Runtime configuration is in `src/GPTDeskTop/appsettings.json`.

Key settings:

- `OpenAI:Model`: defaults to `chat-latest`; change to another model your API project can access.
- `OpenAI:Endpoint`: defaults to `https://api.openai.com/v1/chat/completions`.
- `OpenAI:TimeoutSeconds`: request timeout.
- `Monitoring:PollIntervalSeconds`: polling tick interval.
- `Monitoring:SimulationEnabled`: set `false` when a real `IMessageSource` is connected.
- `Monitoring:SimulationMessageIntervalSeconds`: simulated inbound-message interval.
- `Database:FileName`: defaults to `appdata.db` in the executable folder.

`OPENAI_API_KEY` takes precedence over the JSON `OpenAI:ApiKey` value.

## Database

At application startup, `DatabaseInitializer` creates the SQLite file and required tables if they do not exist. SQLite WAL mode and a busy timeout are enabled for better desktop concurrency behavior.

Tables:

- `MessageLogs`: `Id`, `Timestamp`, `Direction`, `Prompt`, `Response`, `Status`.
- `AppSettings`: `Key`, `Value`.

Local `*.db` files are ignored by Git.

## Real Chat Integration

The project intentionally does not assume which external chat platform is being monitored. The included `SimulatedMessageSource` is a safe test adapter.

To connect a real system:

1. Create a class implementing `IMessageSource`.
2. Poll or consume the upstream system exclusively through its supported API/queue mechanism.
3. Return one new inbound message from `TryReceiveAsync` or `null` when none is available.
4. Register that implementation in `Program.cs`.
5. Set `SimulationEnabled` to `false`.

This preserves background isolation and avoids keyboard/mouse/window automation.

## Build Release

```powershell
dotnet publish .\src\GPTDeskTop\GPTDeskTop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true
```

Output is created under the project's `bin\Release\net8.0-windows\win-x64\publish` directory.

## Smoke Test Checklist

- [ ] Delete `appdata.db`, launch app, verify DB is recreated.
- [ ] Start monitoring, verify UI remains responsive.
- [ ] Verify an inbound simulated message appears in Live Activity.
- [ ] Verify OpenAI response appears as Outbound.
- [ ] Verify two corresponding DB/history rows exist.
- [ ] Stop monitoring and confirm no further polling occurs.
- [ ] Restart app and confirm history persists.
- [ ] Remove API key and confirm a visible error is logged without app crash.
- [ ] Test offline/network timeout behavior.

## Security Notes

Do not commit real API keys. Prefer `OPENAI_API_KEY`. For production deployment, consider Windows Credential Manager, DPAPI, or an enterprise secrets provider if users must configure credentials through the UI.

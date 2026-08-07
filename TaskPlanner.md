# GPTDeskTop Project Implementation Plan

## 1. Objective
Build a production-structured .NET 8 Windows Forms desktop application that monitors an inbound message source, automatically sends received prompts to OpenAI through the Chat Completions REST API, stores local audit/history data in SQLite, and updates the user interface without blocking the UI thread.

## 2. Architecture

- **Presentation:** WinForms `MainForm` with Start, Stop, Refresh History, live activity log, and message history grid.
- **Application/Workflow:** `MonitorService` owns the cancellable polling loop and message-processing orchestration.
- **AI Integration:** `GptService` uses `HttpClient`, Bearer authentication, configurable endpoint/model, timeout handling, and response validation.
- **Message Source Abstraction:** `IMessageSource` allows the simulator to be replaced later by a real API/webhook/queue integration without changing the UI or OpenAI service.
- **Persistence:** EF Core 8 + SQLite in a local `appdata.db` file in the application directory.
- **Configuration:** JSON configuration plus environment-variable override for `OPENAI_API_KEY`.
- **Concurrency:** `PeriodicTimer`, `Task.Run`, async I/O, `CancellationTokenSource`, and UI marshaling through `BeginInvoke`.
- **Reliability:** startup database initialization, WAL mode, database busy timeout, guarded API calls, graceful cancellation, error logging, and non-fatal history refresh failures.

## 3. Implementation Phases

1. **Foundation:** solution/project, NuGet dependencies, configuration model, `.gitignore`.
2. **Persistence:** entities, EF Core context, automatic SQLite initialization, repositories.
3. **OpenAI Integration:** REST client, authentication, serialization, timeout/error handling.
4. **Monitoring Engine:** message-source abstraction, simulated source, polling loop, cancellation lifecycle.
5. **Desktop UI:** toolbar controls, live log, history DataGridView, thread-safe updates.
6. **Operational Hardening:** graceful shutdown, safe API-key handling, logging/error visibility.
7. **Documentation/Validation:** README, setup commands, test checklist, future integration notes.

## 4. Task Tracking Table

| Task ID | Task Name & Description | Suggested Role/Assignee | Priority | Status | Module / File Affected |
|---|---|---|---|---|---|
| ARC-001 | Create .NET 8 WinForms solution and modular folder structure | Tech Lead / Backend Engineer | High | Done | `GPTDeskTop.sln`, `GPTDeskTop.csproj` |
| CFG-001 | Implement typed configuration for OpenAI, monitoring, and database settings | Backend Engineer | High | Done | `Configuration/AppConfig.cs`, `appsettings.json` |
| SEC-001 | Load API key from environment variable with JSON fallback; exclude secrets/local DB from Git | Security Engineer / Backend Engineer | High | Done | `Program.cs`, `.gitignore`, `appsettings.json.template` |
| DB-001 | Define MessageLog schema with Id, Timestamp, Direction, Prompt, Response, Status | Database Admin / Backend Engineer | High | Done | `Models/MessageLog.cs` |
| DB-002 | Define key-value application settings table | Database Admin / Backend Engineer | Medium | Done | `Models/AppSetting.cs` |
| DB-003 | Implement EF Core SQLite DbContext | Database Admin / Backend Engineer | High | Done | `Data/AppDbContext.cs` |
| DB-004 | Automatically create/initialize `appdata.db` and tables on startup | Database Admin / Backend Engineer | High | Done | `Data/DatabaseInitializer.cs` |
| DB-005 | Add repository for message history writes and newest-first reads | Backend Engineer | High | Done | `Data/MessageRepository.cs` |
| DB-006 | Add key-value settings repository and seed runtime settings on startup | Backend Engineer / Database Admin | Medium | Done | `Data/AppSettingsRepository.cs`, `Data/DatabaseInitializer.cs` |
| AI-001 | Implement OpenAI Chat Completions REST client with HttpClient/Bearer auth | Backend Engineer | High | Done | `Services/GptService.cs` |
| AI-002 | Add API timeout, HTTP error reporting, JSON validation, cancellation | Backend Engineer / QA | High | Done | `Services/GptService.cs` |
| MSG-001 | Define replaceable inbound message-source interface | Solution Architect | High | Done | `Services/IMessageSource.cs` |
| MSG-002 | Add simulated inbound message source for end-to-end testing | Backend Engineer / QA | Medium | Done | `Services/SimulatedMessageSource.cs` |
| MON-001 | Implement PeriodicTimer monitoring loop and graceful cancellation | Backend Engineer | High | Done | `Services/MonitorService.cs` |
| MON-002 | Persist inbound/outbound audit rows and broadcast live activity events | Backend Engineer | High | Done | `Services/MonitorService.cs` |
| UI-001 | Build Start Monitoring / Stop Monitoring / Refresh History controls | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-002 | Build timestamped live RichTextBox activity display | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-003 | Build sortable/read-only DataGridView history with required columns | UI Developer | High | Done | `UI/MainForm.cs` |
| UI-004 | Marshal background events safely to WinForms UI thread | UI Developer / Backend Engineer | High | Done | `UI/MainForm.cs` |
| OPS-001 | Handle graceful application shutdown while monitor is active | Backend Engineer / QA | High | Done | `UI/MainForm.cs` |
| DOC-001 | Add setup/run/configuration documentation and test checklist | Technical Writer / Tech Lead | Medium | Done | `README.md`, `TaskPlanner.md` |
| INT-001 | Replace simulated source with actual source-specific chat polling/webhook adapter | Integration Engineer | High | Not Started | New `Services/*MessageSource.cs` |
| QA-001 | Build on Windows with .NET 8 SDK and execute smoke tests | QA Engineer | High | Not Started | Whole solution |
| QA-002 | Validate OpenAI credentials/model access against target account | QA / Backend Engineer | High | Not Started | Runtime configuration |
| REL-001 | Create signed/published Windows release installer | DevOps / Release Engineer | Medium | Not Started | Publish pipeline / installer |

## 5. Acceptance Criteria

- App starts without a pre-existing database and creates `appdata.db` automatically.
- Start Monitoring runs work asynchronously and UI remains responsive.
- Stop Monitoring cancels and exits the loop gracefully.
- Simulated inbound prompt can flow through OpenAI and be stored as inbound/outbound records.
- Live log and history grid update safely from background processing.
- History is displayed newest first.
- Missing API key and API/network failures are visible without crashing the application.
- Database/API/local configuration artifacts that should not be committed are ignored.
- A real chat source can be integrated by implementing `IMessageSource` without rewriting the UI or monitoring engine.

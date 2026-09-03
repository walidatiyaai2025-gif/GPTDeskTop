using System.Diagnostics;
using System.Reflection;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Services;
using GPTDeskTop.Services.DevelopmentTaskEngine;
using GPTDeskTop.UI;

namespace GPTDeskTop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var startupTimer = Stopwatch.StartNew();

        if (CrashRecoveryProcessProbe.IsProbeCommand(args))
        {
            Environment.ExitCode = CrashRecoveryProcessProbe.Run(args);
            return;
        }

        if (HiddenChromeProcessProbe.IsProbeCommand(args))
        {
            Environment.ExitCode = HiddenChromeProcessProbe.Run(args);
            return;
        }

        if (NoResponseWatchdogProcessProbe.IsProbeCommand(args))
        {
            Environment.ExitCode = NoResponseWatchdogProcessProbe.Run(args);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        var instanceStartup = InstanceHandoffCoordinator.AcquireOrTakeOver();
        if (!instanceStartup.IsPrimary)
        {
            MessageBox.Show(
                instanceStartup.Error ?? "Another GPTDeskTop instance is already active and a safe takeover could not be completed.",
                "GPTDeskTop Instance Handoff",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var instanceHandoff = instanceStartup.Coordinator!;
        var takeover = instanceStartup.Takeover;
        LocalDatabase? database = null;
        DevelopmentTaskRuntimeBinding? developmentRuntime = null;

        try
        {
            var config = takeover?.Config ?? AppConfig.Load();
            var requestedDatabasePath = !string.IsNullOrWhiteSpace(takeover?.DatabasePath)
                ? takeover.DatabasePath
                : config.Database.FileName;
            var databasePath = ResolveDatabasePath(requestedDatabasePath!);
            config.Database.FileName = databasePath;

            database = new LocalDatabase(databasePath);
            database.InitializeAsync().GetAwaiter().GetResult();
            DatabasePerformanceInitializer.ApplyAsync(databasePath).GetAwaiter().GetResult();

            if (database.GetSettingAsync("NoResponseRefreshSeconds").GetAwaiter().GetResult() is null)
                database.SetSettingAsync("NoResponseRefreshSeconds", "180").GetAwaiter().GetResult();

            // Exception logging is shared infrastructure required by Monitor Only itself. No legacy
            // Current GPTDeskTop service, recovery worker, saved monitor, or development runtime is
            // constructed before the explicit Current GPTDeskTop radio authorization below.
            ExceptionLogService.Configure(database);
            Application.ThreadException += (_, e) => ExceptionLogService.Log(e.Exception, "Application.ThreadException");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception exception) ExceptionLogService.Log(exception, "AppDomain.UnhandledException");
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ExceptionLogService.Log(e.Exception, "TaskScheduler.UnobservedTaskException");
                e.SetObserved();
            };

            // TRUE MODE GATE: Monitor Only owns the first application message loop. Closing it while
            // Monitor Only remains selected exits GPTDeskTop. Only selecting Current GPTDeskTop allows
            // execution to continue to any legacy business below this point.
            if (!MonitorOnlyStartupGate.Run(database))
                return;

            // Do not count time intentionally spent operating Monitor Only against legacy UI startup.
            startupTimer.Restart();

            // Everything below this line belongs to Current GPTDeskTop and is intentionally lazy.
            var currentStartupWasUnclean = CrashRecoveryStateService.PrepareStartupAsync(database).GetAwaiter().GetResult();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var chrome = new ChromeDevToolsService(httpClient, config.Chrome);
            var monitor = new ChatGptMonitorService(chrome, database, config.Monitoring);
            using var notifications = new TrayNotificationService(monitor, database);
            notifications.InitializeAsync().GetAwaiter().GetResult();

            var mainForm = new MainForm(chrome, monitor, database, notifications.ReloadSettingsAsync);
            ProjectMonitorUiBootstrap.Install(mainForm);
            if (ApplicationBuildIdentity.StableBuildId is not null)
                mainForm.Text = $"GPTDeskTop {ApplicationBuildIdentity.DisplayVersion}";

            // The development runtime is created only after Current GPTDeskTop was explicitly selected.
            var resolver = new SavedMonitorTabResolver(chrome);
            var targetFactory = new DevelopmentTaskMonitorTargetFactory(database, resolver, chrome);
            var developmentEngine = new DevelopmentTaskEngine();
            developmentRuntime = new DevelopmentTaskRuntimeBinding(developmentEngine, targetFactory);
            var developmentDashboardExpanded = !string.Equals(
                database.GetSettingAsync("Ui.DevelopmentDashboard.Expanded").GetAwaiter().GetResult(),
                "0",
                StringComparison.Ordinal);
            var developmentDashboard = new DevelopmentTaskDashboardControl(developmentRuntime)
            {
                Dock = DockStyle.Top,
                TabStop = false,
                IsExpanded = developmentDashboardExpanded
            };
            developmentDashboard.ExpandedChanged += async (_, _) =>
            {
                try
                {
                    await database.SetSettingAsync(
                        "Ui.DevelopmentDashboard.Expanded",
                        developmentDashboard.IsExpanded ? "1" : "0");
                }
                catch (Exception ex)
                {
                    await ExceptionLogService.LogAsync(ex, "Program.PersistDevelopmentDashboardState");
                }
            };
            mainForm.Controls.Add(developmentDashboard);
            mainForm.Controls.SetChildIndex(developmentDashboard, 0);

            var runtimeHealthExpanded = string.Equals(
                database.GetSettingAsync("Ui.RuntimeHealth.Expanded").GetAwaiter().GetResult(),
                "1",
                StringComparison.Ordinal);
            var runtimeHealth = new RuntimeHealthControl(chrome, monitor, database)
            {
                Dock = DockStyle.Top,
                TabStop = false,
                IsExpanded = runtimeHealthExpanded
            };

            // Support diagnostics is relatively heavy (bundle/service UI) and is not runtime-critical.
            // Create it only when Runtime Health details are actually opened.
            SupportDiagnosticsControl? supportDiagnostics = null;
            void EnsureSupportDiagnostics()
            {
                if (supportDiagnostics is not null && !supportDiagnostics.IsDisposed)
                    return;

                var supportBundleService = new SupportBundleService(chrome, monitor, database, config);
                supportDiagnostics = new SupportDiagnosticsControl(supportBundleService)
                {
                    Dock = DockStyle.Top,
                    TabStop = false,
                    Visible = runtimeHealth.IsExpanded
                };
                mainForm.Controls.Add(supportDiagnostics);
                mainForm.Controls.SetChildIndex(supportDiagnostics, 0);
            }

            runtimeHealth.ExpandedChanged += async (_, _) =>
            {
                if (runtimeHealth.IsExpanded)
                    EnsureSupportDiagnostics();
                if (supportDiagnostics is not null && !supportDiagnostics.IsDisposed)
                    supportDiagnostics.Visible = runtimeHealth.IsExpanded;

                try
                {
                    await database.SetSettingAsync(
                        "Ui.RuntimeHealth.Expanded",
                        runtimeHealth.IsExpanded ? "1" : "0");
                }
                catch (Exception ex)
                {
                    await ExceptionLogService.LogAsync(ex, "Program.PersistRuntimeHealthState");
                }
            };
            mainForm.Controls.Add(runtimeHealth);
            mainForm.Controls.SetChildIndex(runtimeHealth, 0);
            if (runtimeHealthExpanded)
                EnsureSupportDiagnostics();

            // MainForm already owns the canonical Stored History surface. The former additional
            // HistoryWorkspaceControl duplicated a second grid, event wiring and 500-row refresh at
            // cold start, so it is intentionally no longer constructed here.

            using var metrics = new HomeMetricsService(mainForm, database, monitor);

            instanceHandoff.StartTakeoverServer(
                async cancellationToken =>
                {
                    if (mainForm.IsDisposed || mainForm.Disposing)
                        throw new InvalidOperationException("The current GPTDeskTop instance is already shutting down.");

                    await PersistOperatorLayoutForInstanceHandoffAsync(mainForm, cancellationToken);

                    var savedMonitors = await database.GetSavedMonitorsAsync(cancellationToken);
                    var runningMonitorIds = savedMonitors
                        .Where(saved => monitor.IsMonitorRunning(saved.Id))
                        .Select(saved => saved.Id)
                        .OrderBy(id => id)
                        .ToArray();
                    return new InstanceHandoffState(databasePath, config, runningMonitorIds);
                },
                () => CompleteCommittedInstanceHandoffAsync(database, monitor, developmentRuntime));

            mainForm.Shown += async (_, _) =>
            {
                startupTimer.Stop();
                try
                {
                    await database.SetSettingAsync("Runtime.LastUiStartupMs", startupTimer.ElapsedMilliseconds.ToString());
                    await database.SetSettingAsync(
                        "Runtime.LastUiStartupBudget",
                        startupTimer.ElapsedMilliseconds <= 3000 ? "PASS" : "WARN");
                }
                catch (Exception ex)
                {
                    await ExceptionLogService.LogAsync(ex, "Program.RecordUiStartupBudget");
                }

                try
                {
                    var recoveryMode = currentStartupWasUnclean
                        ? CrashRecoveryMode.FreshCrashReset
                        : CrashRecoveryMode.PendingRetry;
                    await CrashRecoveryService.RecoverIfPendingAsync(
                        chrome,
                        monitor,
                        database,
                        recoveryMode);

                    if (takeover is not null)
                    {
                        var reconciliation = await InstanceHandoffCoordinator.ResumeRunningMonitorsAsync(
                            takeover,
                            chrome,
                            monitor,
                            database);
                        var incompleteIds = string.Join(",", reconciliation.IncompleteMonitorIds);

                        await database.SetSettingAsync("LastInstanceHandoffUtc", DateTimeOffset.UtcNow.ToString("O"));
                        await database.SetSettingAsync("LastInstanceHandoffRequestedCount", reconciliation.RequestedCount.ToString());
                        await database.SetSettingAsync("LastInstanceHandoffResumedCount", reconciliation.ResumedCount.ToString());
                        await database.SetSettingAsync("LastInstanceHandoffIncompleteCount", reconciliation.IncompleteCount.ToString());
                        await database.SetSettingAsync("LastInstanceHandoffIncompleteIds", incompleteIds);

                        if (reconciliation.IncompleteCount > 0)
                        {
                            var summary = string.Join(
                                "; ",
                                reconciliation.Outcomes
                                    .Where(outcome => !string.Equals(outcome.Status, "Resumed", StringComparison.Ordinal))
                                    .Select(outcome => $"{outcome.MonitorId}:{outcome.Reason}"));
                            await ExceptionLogService.LogAsync(
                                new InvalidOperationException(
                                    $"Instance takeover resumed {reconciliation.ResumedCount}/{reconciliation.RequestedCount} requested monitors. Incomplete outcomes: {summary}"),
                                "Program.InstanceHandoffResumeIncomplete");
                        }

                        await LastWorkingStateService.ReplaceDesiredMonitorIdsAsync(
                            database,
                            takeover.RunningMonitorIds);
                    }
                    else
                    {
                        var resume = await LastWorkingStateService.ResumeDesiredMonitorsAsync(
                            chrome,
                            monitor,
                            database);
                        if (resume.IncompleteCount > 0)
                        {
                            var summary = string.Join(
                                "; ",
                                resume.Outcomes
                                    .Where(outcome => !string.Equals(outcome.Status, "Resumed", StringComparison.Ordinal))
                                    .Select(outcome => $"{outcome.MonitorId}:{outcome.Reason}"));
                            await ExceptionLogService.LogAsync(
                                new InvalidOperationException(
                                    $"Restart resume restored {resume.ResumedCount}/{resume.RequestedCount} desired monitors. Incomplete outcomes: {summary}"),
                                "Program.LastWorkingStateResumeIncomplete");
                        }
                    }

                    if (developmentRuntime is not null)
                    {
                        var developmentResumed = await developmentRuntime.ResumeIfActiveAsync();
                        await database.SetSettingAsync(
                            "Runtime.DevelopmentTaskAutoResumed",
                            developmentResumed ? "1" : "0");
                        if (developmentResumed)
                            await database.SetSettingAsync("Runtime.DevelopmentTaskAutoResumeUtc", DateTimeOffset.UtcNow.ToString("O"));
                    }
                }
                catch (Exception ex)
                {
                    await ExceptionLogService.LogAsync(ex, takeover is null
                        ? "Program.BackgroundCrashRecovery"
                        : "Program.InstanceHandoffResume");
                }
            };

            Application.Run(mainForm);
            Task.Run(() => FinalizeGracefulShutdownAsync(database, developmentRuntime)).GetAwaiter().GetResult();
            developmentRuntime = null;
        }
        catch (Exception ex)
        {
            if (database is not null) ExceptionLogService.Log(ex, "Program.Main.Fatal");
            else
            {
                try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), $"[{DateTime.Now:O}] {ex}{Environment.NewLine}"); }
                catch { }
            }

            try
            {
                developmentRuntime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { }

            var restarted = TryRestartAfterFatal(database);
            if (!restarted)
            {
                MessageBox.Show(
                    $"GPTDeskTop encountered a fatal error.\n\n{ex.Message}\n\nThe exception was written to the application log.",
                    "GPTDeskTop Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    private static string ResolveDatabasePath(string fileName)
    {
        var path = Path.IsPathRooted(fileName)
            ? fileName
            : Path.Combine(AppContext.BaseDirectory, fileName);
        return Path.GetFullPath(path);
    }

    private static async Task PersistOperatorLayoutForInstanceHandoffAsync(
        MainForm mainForm,
        CancellationToken cancellationToken)
    {
        var method = typeof(MainForm).GetMethod(
            "PersistOperatorLayoutAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainForm).FullName, "PersistOperatorLayoutAsync");

        async Task PersistOnUiThreadAsync()
        {
            var result = method.Invoke(mainForm, new object[] { cancellationToken });
            if (result is not Task task)
                throw new InvalidOperationException("MainForm layout persistence did not return a Task.");
            await task;
        }

        if (!mainForm.InvokeRequired)
        {
            await PersistOnUiThreadAsync();
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mainForm.BeginInvoke(new Action(async () =>
        {
            try
            {
                await PersistOnUiThreadAsync();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }));

        await completion.Task.WaitAsync(cancellationToken);
    }

    private static async Task CompleteCommittedInstanceHandoffAsync(
        LocalDatabase database,
        ChatGptMonitorService monitor,
        DevelopmentTaskRuntimeBinding? developmentRuntime)
    {
        try
        {
            if (monitor.IsRunning)
                await monitor.StopAllAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "InstanceHandoff.StopMonitorWorkers");
        }

        try
        {
            await CrashRecoveryStateService.MarkCleanShutdownAsync(database)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "InstanceHandoff.MarkCleanShutdown");
        }

        if (developmentRuntime is not null)
        {
            try
            {
                await developmentRuntime.DisposeAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ExceptionLogService.Log(ex, "InstanceHandoff.DisposeDevelopmentRuntime");
            }
        }

        Environment.Exit(0);
    }

    private static async Task FinalizeGracefulShutdownAsync(LocalDatabase database, DevelopmentTaskRuntimeBinding? developmentRuntime)
    {
        try
        {
            await CrashRecoveryStateService.MarkCleanShutdownAsync(database)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "Program.MarkCleanShutdown");
        }

        if (developmentRuntime is null) return;

        try
        {
            await developmentRuntime.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "Program.DisposeDevelopmentRuntime");
        }
    }

    private static bool TryRestartAfterFatal(LocalDatabase? database)
    {
        try
        {
            if (database is not null)
            {
                var raw = database.GetSettingAsync("LastFatalRestartUtc").GetAwaiter().GetResult();
                if (DateTimeOffset.TryParse(raw, out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(30))
                    return false;
                database.SetSettingAsync("LastFatalRestartUtc", DateTimeOffset.UtcNow.ToString("O")).GetAwaiter().GetResult();
                database.SetSettingAsync("CrashRecoveryPending", "1").GetAwaiter().GetResult();
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return false;
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}

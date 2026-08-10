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

            if (database.GetSettingAsync("NoResponseRefreshSeconds").GetAwaiter().GetResult() is null)
                database.SetSettingAsync("NoResponseRefreshSeconds", "180").GetAwaiter().GetResult();

            var currentStartupWasUnclean = CrashRecoveryStateService.PrepareStartupAsync(database).GetAwaiter().GetResult();
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

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var chrome = new ChromeDevToolsService(httpClient, config.Chrome);
            var monitor = new ChatGptMonitorService(chrome, database, config.Monitoring);
            using var notifications = new TrayNotificationService(monitor, database);
            notifications.InitializeAsync().GetAwaiter().GetResult();

            var mainForm = new MainForm(chrome, monitor, database, notifications.ReloadSettingsAsync);
            if (ApplicationBuildIdentity.StableBuildId is not null)
                mainForm.Text = $"GPTDeskTop {ApplicationBuildIdentity.DisplayVersion}";

            // Production development-plan runtime: the dashboard and lifecycle controls
            // are bound to dynamic saved-monitor resolution before the UI is shown.
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
            var supportBundleService = new SupportBundleService(chrome, monitor, database, config);
            var supportDiagnostics = new SupportDiagnosticsControl(supportBundleService)
            {
                Dock = DockStyle.Top,
                TabStop = false,
                Visible = runtimeHealthExpanded
            };
            runtimeHealth.ExpandedChanged += async (_, _) =>
            {
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
            mainForm.Controls.Add(supportDiagnostics);
            mainForm.Controls.SetChildIndex(supportDiagnostics, 0);
            mainForm.Controls.Add(runtimeHealth);
            mainForm.Controls.SetChildIndex(runtimeHealth, 0);

            var historyWorkspaceExpanded = string.Equals(
                database.GetSettingAsync("Ui.HistoryWorkspace.Expanded").GetAwaiter().GetResult(),
                "1",
                StringComparison.Ordinal);
            var historyWorkspace = new HistoryWorkspaceControl(database)
            {
                Dock = DockStyle.Bottom,
                TabStop = false,
                IsExpanded = historyWorkspaceExpanded
            };
            historyWorkspace.ExpandedChanged += async (_, _) =>
            {
                try
                {
                    await database.SetSettingAsync(
                        "Ui.HistoryWorkspace.Expanded",
                        historyWorkspace.IsExpanded ? "1" : "0");
                }
                catch (Exception ex)
                {
                    await ExceptionLogService.LogAsync(ex, "Program.PersistHistoryWorkspaceState");
                }
            };
            mainForm.Controls.Add(historyWorkspace);
            mainForm.Controls.SetChildIndex(historyWorkspace, 0);

            using var metrics = new HomeMetricsService(mainForm, database, monitor);

            instanceHandoff.StartTakeoverServer(
                async cancellationToken =>
                {
                    if (mainForm.IsDisposed || mainForm.Disposing)
                        throw new InvalidOperationException("The current GPTDeskTop instance is already shutting down.");

                    // Persist the live window/splitter layout before the offer is ACKed so the
                    // replacement process restores the latest operator workspace, not only the
                    // last layout captured by a normal application exit.
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
                        var resumed = await InstanceHandoffCoordinator.ResumeRunningMonitorsAsync(
                            takeover,
                            chrome,
                            monitor,
                            database);
                        await database.SetSettingAsync("LastInstanceHandoffUtc", DateTimeOffset.UtcNow.ToString("O"));
                        await database.SetSettingAsync("LastInstanceHandoffResumedCount", resumed.ToString());
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

            try { developmentRuntime?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
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

        // Deliberately bypass MainForm's normal close path here. Normal close tears down
        // monitor Chrome tabs; a committed instance takeover must leave those tabs and any
        // in-progress ChatGPT generation alive for the new process to reattach safely.
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

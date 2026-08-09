using System.Diagnostics;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Services;
using GPTDeskTop.Services.DevelopmentTaskEngine;
using GPTDeskTop.UI;

namespace GPTDeskTop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        LocalDatabase? database = null;
        DevelopmentTaskRuntimeBinding? developmentRuntime = null;

        try
        {
            var config = AppConfig.Load();
            database = new LocalDatabase(config.Database.FileName);
            database.InitializeAsync().GetAwaiter().GetResult();

            if (database.GetSettingAsync("NoResponseRefreshSeconds").GetAwaiter().GetResult() is null)
                database.SetSettingAsync("NoResponseRefreshSeconds", "180").GetAwaiter().GetResult();

            CrashRecoveryStateService.PrepareStartupAsync(database).GetAwaiter().GetResult();
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

            var mainForm = new MainForm(chrome, monitor, database);

            // Production development-plan runtime: the dashboard and lifecycle controls
            // are bound to dynamic saved-monitor resolution before the UI is shown.
            var resolver = new SavedMonitorTabResolver(chrome);
            var targetFactory = new DevelopmentTaskMonitorTargetFactory(database, resolver, chrome);
            var developmentEngine = new DevelopmentTaskEngine();
            developmentRuntime = new DevelopmentTaskRuntimeBinding(developmentEngine, targetFactory);
            var developmentDashboard = new DevelopmentTaskDashboardControl(developmentRuntime)
            {
                Height = 190,
                Dock = DockStyle.Top,
                TabStop = false
            };
            mainForm.Controls.Add(developmentDashboard);
            mainForm.Controls.SetChildIndex(developmentDashboard, 0);

            using var metrics = new HomeMetricsService(mainForm, database, monitor);

            mainForm.Shown += async (_, _) =>
            {
                try
                {
                    await CrashRecoveryService.RecoverIfPendingAsync(chrome, monitor, database);
                }
                catch (Exception ex)
                {
                    await ExceptionLogService.LogAsync(ex, "Program.BackgroundCrashRecovery");
                }
            };

            mainForm.FormClosed += (_, _) =>
            {
                try { CrashRecoveryStateService.MarkCleanShutdownAsync(database).GetAwaiter().GetResult(); }
                catch (Exception ex) { ExceptionLogService.Log(ex, "Program.MarkCleanShutdown"); }
                try { developmentRuntime?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch (Exception ex) { ExceptionLogService.Log(ex, "Program.DisposeDevelopmentRuntime"); }
                developmentRuntime = null;
            };

            Application.Run(mainForm);
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

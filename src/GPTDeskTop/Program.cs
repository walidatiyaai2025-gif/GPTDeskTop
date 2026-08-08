using System.Diagnostics;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Services;
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

        try
        {
            var config = AppConfig.Load();
            database = new LocalDatabase(config.Database.FileName);
            database.InitializeAsync().GetAwaiter().GetResult();

            if (database.GetSettingAsync("NoResponseRefreshSeconds").GetAwaiter().GetResult() is null)
                database.SetSettingAsync("NoResponseRefreshSeconds", "180").GetAwaiter().GetResult();

            if (database.GetSettingAsync("TaskAutomation.WorkWindowMinutes").GetAwaiter().GetResult() is null)
                database.SetSettingAsync("TaskAutomation.WorkWindowMinutes", config.TaskAutomation.WorkWindowMinutes.ToString()).GetAwaiter().GetResult();
            if (database.GetSettingAsync("TaskAutomation.CoolingWindowMinutes").GetAwaiter().GetResult() is null)
                database.SetSettingAsync("TaskAutomation.CoolingWindowMinutes", config.TaskAutomation.CoolingWindowMinutes.ToString()).GetAwaiter().GetResult();

            var previousClean = database.GetSettingAsync("LastShutdownClean").GetAwaiter().GetResult();
            if (string.Equals(previousClean, "0", StringComparison.Ordinal))
            {
                var crashes = database.GetIntSettingAsync("CrashCount", 0, 0, int.MaxValue).GetAwaiter().GetResult();
                database.SetSettingAsync("CrashCount", (crashes + 1).ToString()).GetAwaiter().GetResult();
                database.SetSettingAsync("CrashRecoveryPending", "1").GetAwaiter().GetResult();
            }
            else if (previousClean is null)
            {
                database.SetSettingAsync("CrashCount", "0").GetAwaiter().GetResult();
                database.SetSettingAsync("CrashRecoveryPending", "0").GetAwaiter().GetResult();
            }

            database.SetSettingAsync("LastShutdownClean", "0").GetAwaiter().GetResult();
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

            var taskAutomation = new TaskAutomationService(chrome, database, config.TaskAutomation);
            var mainForm = new MainForm(chrome, monitor, database);
            using var metrics = new HomeMetricsService(mainForm, database, monitor);

            DevelopmentAutomationLauncher.Attach(mainForm, taskAutomation, database);
            mainForm.KeyPreview = true;
            mainForm.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.F12) return;
                e.Handled = true;
                using var form = new DevelopmentAutomationForm(taskAutomation, database);
                form.ShowDialog(mainForm);
            };

            mainForm.Shown += async (_, _) =>
            {
                try
                {
                    await CrashRecoveryService.RecoverIfPendingAsync(chrome, monitor, database);

                    if (config.TaskAutomation.Enabled && config.TaskAutomation.ResumeOnStartup)
                    {
                        var phase = await database.GetSettingAsync("TaskAutomation.Phase");
                        if (TaskAutomationStartupPolicy.ShouldResume(phase))
                            await taskAutomation.StartAsync();
                    }
                }
                catch (Exception ex)
                {
                    await ExceptionLogService.LogAsync(ex, "Program.BackgroundStartupRecovery");
                }
            };

            mainForm.FormClosed += (_, _) =>
            {
                try
                {
                    taskAutomation.StopAsync().GetAwaiter().GetResult();
                    database.SetSettingAsync("LastShutdownClean", "1").GetAwaiter().GetResult();
                }
                catch (Exception ex) { ExceptionLogService.Log(ex, "Program.MarkCleanShutdown"); }
            };

            Application.Run(mainForm);
            taskAutomation.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            if (database is not null) ExceptionLogService.Log(ex, "Program.Main.Fatal");
            else
            {
                try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), $"[{DateTime.Now:O}] {ex}{Environment.NewLine}"); }
                catch { }
            }

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

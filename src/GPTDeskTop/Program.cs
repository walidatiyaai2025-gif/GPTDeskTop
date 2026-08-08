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

            // IMPORTANT: Never block startup on Chrome/CDP recovery. The tray icon used to
            // appear while RecoverIfPendingAsync was waiting for Chrome, making the main form
            // look as if it had failed to load. Show the UI first and recover asynchronously.
            var mainForm = new MainForm(chrome, monitor, database);
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

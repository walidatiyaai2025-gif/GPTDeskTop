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
                if (e.ExceptionObject is Exception exception)
                    ExceptionLogService.Log(exception, "AppDomain.UnhandledException");
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

            CrashRecoveryService.RecoverIfPendingAsync(chrome, monitor, database).GetAwaiter().GetResult();

            var mainForm = new MainForm(chrome, monitor, database);
            using var metrics = new HomeMetricsService(mainForm, database, monitor);
            mainForm.FormClosed += (_, _) =>
            {
                try { database.SetSettingAsync("LastShutdownClean", "1").GetAwaiter().GetResult(); }
                catch (Exception ex) { ExceptionLogService.Log(ex, "Program.MarkCleanShutdown"); }
            };

            Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            if (database is not null)
                ExceptionLogService.Log(ex, "Program.Main");
            else
            {
                try
                {
                    var emergencyPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
                    File.AppendAllText(emergencyPath, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
                }
                catch { }
            }

            MessageBox.Show(
                $"GPTDeskTop encountered a fatal error.\n\n{ex.Message}\n\nThe exception was written to the application log.",
                "GPTDeskTop Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

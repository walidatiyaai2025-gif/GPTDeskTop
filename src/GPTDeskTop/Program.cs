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

            ExceptionLogService.Configure(database);

            Application.ThreadException += (_, e) =>
                ExceptionLogService.Log(e.Exception, "Application.ThreadException");

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

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            var chrome = new ChromeDevToolsService(httpClient, config.Chrome);
            var monitor = new ChatGptMonitorService(chrome, database, config.Monitoring);
            using var notifications = new TrayNotificationService(monitor, database);
            notifications.InitializeAsync().GetAwaiter().GetResult();

            Application.Run(new MainForm(chrome, monitor, database));
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

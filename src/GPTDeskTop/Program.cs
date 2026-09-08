using System.Diagnostics;
using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Services;
using GPTDeskTop.UI;

namespace GPTDeskTop;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\GPTDeskTop-MonitorOnly-SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        // Dedicated probe processes are test/diagnostic entry points and must not contend with the
        // interactive Monitor Only singleton.
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

        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show(
                "Monitor Only is already running. GPTDeskTop allows exactly one interactive instance so a second process cannot create another browser sender.",
                "Monitor Only",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        LocalDatabase? database = null;
        try
        {
            var config = AppConfig.Load();
            var databasePath = ResolveDatabasePath(config.Database.FileName!);
            config.Database.FileName = databasePath;

            database = new LocalDatabase(databasePath);
            database.InitializeAsync().GetAwaiter().GetResult();
            DatabasePerformanceInitializer.ApplyAsync(databasePath).GetAwaiter().GetResult();

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

            // v2.0.41 hard cutover: Monitor Only is the product. There is no Current GPTDeskTop
            // continuation path after this message loop and no legacy saved-monitor/development
            // runtime is ever constructed by normal application startup.
            MonitorOnlyStartupGate.Run(database);
        }
        catch (Exception ex)
        {
            if (database is not null)
                ExceptionLogService.Log(ex, "Program.Main.Fatal");
            else
            {
                try
                {
                    File.AppendAllText(
                        Path.Combine(AppContext.BaseDirectory, "startup-error.log"),
                        $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
                }
                catch
                {
                }
            }

            MessageBox.Show(
                $"Monitor Only encountered a fatal error.\n\n{ex.Message}\n\nThe exception was written to the application log.",
                "Monitor Only Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            try { singleInstance.ReleaseMutex(); } catch (ApplicationException) { }
        }
    }

    private static string ResolveDatabasePath(string fileName)
    {
        var path = Path.IsPathRooted(fileName)
            ? fileName
            : Path.Combine(AppContext.BaseDirectory, fileName);
        return Path.GetFullPath(path);
    }
}

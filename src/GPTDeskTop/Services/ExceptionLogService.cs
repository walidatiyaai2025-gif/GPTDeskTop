using System.Net.Sockets;
using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

public static class ExceptionLogService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static LocalDatabase? _database;
    private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    public static event Action<string>? Logged;

    public static void Configure(LocalDatabase database)
    {
        _database = database;
        Directory.CreateDirectory(LogDirectory);
    }

    public static async Task LogAsync(Exception exception, string source, long? monitorId = null, string? tabId = null, string? tabTitle = null)
    {
        if (IsExpectedChromeDevToolsOfflineProbe(exception, source))
            return;

        var timestamp = DateTimeOffset.Now;
        var details = $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] Source={source}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 100)}{Environment.NewLine}";

        try
        {
            await Gate.WaitAsync();
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var file = Path.Combine(LogDirectory, $"exceptions-{DateTime.Now:yyyyMMdd}.log");
                await File.AppendAllTextAsync(file, details);
            }
            finally
            {
                Gate.Release();
            }
        }
        catch
        {
            // Exception logging must never crash the application.
        }

        if (_database is not null)
        {
            try
            {
                await _database.AddLogAsync(
                    "System",
                    source,
                    exception.ToString(),
                    "Exception",
                    monitorId,
                    tabId,
                    tabTitle);
            }
            catch
            {
                // File logging above remains the fallback when SQLite is unavailable.
            }
        }

        try { Logged?.Invoke($"{source}: {exception.GetType().Name}: {exception.Message}"); } catch { }
    }

    public static void Log(Exception exception, string source, long? monitorId = null, string? tabId = null, string? tabTitle = null)
        => _ = LogAsync(exception, source, monitorId, tabId, tabTitle);

    private static bool IsExpectedChromeDevToolsOfflineProbe(Exception exception, string source)
    {
        if (!string.Equals(source, "RuntimeHealthControl.ChromeProbe", StringComparison.Ordinal)
            && !string.Equals(source, "SupportBundle.ChromeProbe", StringComparison.Ordinal))
            return false;

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socketException
                && socketException.SocketErrorCode == SocketError.ConnectionRefused)
                return true;
        }

        return false;
    }

    public static string GetTodayLogPath()
        => Path.Combine(LogDirectory, $"exceptions-{DateTime.Now:yyyyMMdd}.log");
}

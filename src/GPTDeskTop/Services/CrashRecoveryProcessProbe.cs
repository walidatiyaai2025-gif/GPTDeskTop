using System.Text.Json;
using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

/// <summary>
/// Headless process-level diagnostic used by Windows CI to exercise the same
/// startup crash marker as the real application without requiring an interactive
/// desktop or a logged-in ChatGPT browser session.
/// </summary>
internal static class CrashRecoveryProcessProbe
{
    private const string ArmCommand = "--qa-crash-probe-arm";
    private const string VerifyCommand = "--qa-crash-probe-verify";

    public static bool IsProbeCommand(string[] args)
        => args.Length > 0 &&
           (string.Equals(args[0], ArmCommand, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(args[0], VerifyCommand, StringComparison.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        if (!IsProbeCommand(args))
            return -1;

        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            try
            {
                var failurePath = args.Length >= 3 ? args[2] + ".error.txt" : Path.Combine(Path.GetTempPath(), "GPTDeskTop-crash-probe-error.txt");
                File.WriteAllText(failurePath, ex.ToString());
            }
            catch
            {
            }

            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 3)
            throw new ArgumentException("Crash recovery probe requires a database path and a signal/result path.");

        var command = args[0];
        var databasePath = Path.GetFullPath(args[1]);
        var outputPath = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? AppContext.BaseDirectory);

        var database = new LocalDatabase(databasePath);
        await database.InitializeAsync().ConfigureAwait(false);
        var wasUnclean = await CrashRecoveryStateService.PrepareStartupAsync(database).ConfigureAwait(false);

        if (string.Equals(command, ArmCommand, StringComparison.OrdinalIgnoreCase))
        {
            var armed = new CrashProbeResult
            {
                ProcessId = Environment.ProcessId,
                WasUnclean = wasUnclean,
                CrashCount = await database.GetIntSettingAsync("CrashCount", 0, 0, int.MaxValue).ConfigureAwait(false),
                CrashRecoveryPending = await database.GetSettingAsync("CrashRecoveryPending").ConfigureAwait(false) ?? string.Empty,
                RecoveryId = await database.GetSettingAsync("CrashRecovery.RecoveryId").ConfigureAwait(false) ?? string.Empty,
                LastShutdownClean = await database.GetSettingAsync("LastShutdownClean").ConfigureAwait(false) ?? string.Empty
            };
            await WriteResultAsync(outputPath, armed).ConfigureAwait(false);

            // Intentionally do not call MarkCleanShutdownAsync. CI force-terminates
            // this process, exactly preserving the marker left by application startup.
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            return 0;
        }

        if (!string.Equals(command, VerifyCommand, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported crash recovery probe command: {command}");

        var result = new CrashProbeResult
        {
            ProcessId = Environment.ProcessId,
            WasUnclean = wasUnclean,
            CrashCount = await database.GetIntSettingAsync("CrashCount", 0, 0, int.MaxValue).ConfigureAwait(false),
            CrashRecoveryPending = await database.GetSettingAsync("CrashRecoveryPending").ConfigureAwait(false) ?? string.Empty,
            RecoveryId = await database.GetSettingAsync("CrashRecovery.RecoveryId").ConfigureAwait(false) ?? string.Empty,
            LastShutdownClean = await database.GetSettingAsync("LastShutdownClean").ConfigureAwait(false) ?? string.Empty
        };
        await WriteResultAsync(outputPath, result).ConfigureAwait(false);
        return 0;
    }

    private static async Task WriteResultAsync(string path, CrashProbeResult result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, json).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    private sealed class CrashProbeResult
    {
        public int ProcessId { get; init; }
        public bool WasUnclean { get; init; }
        public int CrashCount { get; init; }
        public string CrashRecoveryPending { get; init; } = string.Empty;
        public string RecoveryId { get; init; } = string.Empty;
        public string LastShutdownClean { get; init; } = string.Empty;
    }
}

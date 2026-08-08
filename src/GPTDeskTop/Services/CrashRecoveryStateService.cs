using GPTDeskTop.Data;

namespace GPTDeskTop.Services;

public static class CrashRecoveryStateService
{
    public static async Task<bool> PrepareStartupAsync(LocalDatabase database, CancellationToken cancellationToken = default)
    {
        var previousClean = await database.GetSettingAsync("LastShutdownClean", cancellationToken);
        var wasUnclean = string.Equals(previousClean, "0", StringComparison.Ordinal);

        if (wasUnclean)
        {
            var crashes = await database.GetIntSettingAsync("CrashCount", 0, 0, int.MaxValue, cancellationToken);
            await database.SetSettingAsync("CrashCount", (crashes + 1).ToString(), cancellationToken);
            await database.SetSettingAsync("CrashRecoveryPending", "1", cancellationToken);
        }
        else if (previousClean is null)
        {
            await database.SetSettingAsync("CrashCount", "0", cancellationToken);
            await database.SetSettingAsync("CrashRecoveryPending", "0", cancellationToken);
        }

        await database.SetSettingAsync("LastShutdownClean", "0", cancellationToken);
        return wasUnclean;
    }

    public static Task MarkCleanShutdownAsync(LocalDatabase database, CancellationToken cancellationToken = default)
        => database.SetSettingAsync("LastShutdownClean", "1", cancellationToken);
}

using GPTDeskTop.Data;
using GPTDeskTop.Runtime;

namespace GPTDeskTop.Services;

public static class CrashRecoveryStateService
{
    public static async Task<bool> PrepareStartupAsync(LocalDatabase database, CancellationToken cancellationToken = default)
    {
        // Restore the persisted global ChatGPT send breaker before any monitor worker can be created
        // or crash/takeover recovery can resume automated work. This is intentionally fail-closed:
        // an active breaker remains active after process restart until its deadline is probe-eligible
        // and visible ChatGPT state explicitly clears it.
        await GlobalChatGptRateLimitCircuitBreaker.Shared
            .InitializeAsync(database, cancellationToken)
            .ConfigureAwait(false);

        var previousClean = await database.GetSettingAsync("LastShutdownClean", cancellationToken);
        var wasUnclean = string.Equals(previousClean, "0", StringComparison.Ordinal);

        if (wasUnclean)
        {
            var crashes = await database.GetIntSettingAsync("CrashCount", 0, 0, int.MaxValue, cancellationToken);
            await database.SetSettingAsync("CrashCount", (crashes + 1).ToString(), cancellationToken);
            await database.SetSettingAsync("CrashRecoveryPending", "1", cancellationToken);
            await database.SetSettingAsync("CrashRecovery.RecoveryId", Guid.NewGuid().ToString("N"), cancellationToken);
        }
        else if (previousClean is null)
        {
            await database.SetSettingAsync("CrashCount", "0", cancellationToken);
            await database.SetSettingAsync("CrashRecoveryPending", "0", cancellationToken);
            await database.SetSettingAsync("CrashRecovery.RecoveryId", string.Empty, cancellationToken);
        }

        await database.SetSettingAsync("LastShutdownClean", "0", cancellationToken);
        return wasUnclean;
    }

    public static Task MarkCleanShutdownAsync(LocalDatabase database, CancellationToken cancellationToken = default)
        => database.SetSettingAsync("LastShutdownClean", "1", cancellationToken);
}

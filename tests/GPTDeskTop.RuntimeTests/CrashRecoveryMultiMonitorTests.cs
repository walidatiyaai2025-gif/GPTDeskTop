using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoveryMultiMonitorTests
{
    [Fact]
    public void PartialFailureKeepsSuccessfulMonitorIdempotentOnRetry()
    {
        var firstRecovery = new[]
        {
            CrashRecoveryOutcome.Success,
            CrashRecoveryOutcome.SendFailed
        };

        Assert.False(CrashRecoveryOutcomePolicy.ShouldClearPending(firstRecovery));

        // On the retry incident, the successful monitor is represented as an already
        // verified outcome rather than being sent the recovery message again.
        var retryRecovery = new[]
        {
            CrashRecoveryOutcome.Success,
            CrashRecoveryOutcome.Success
        };

        Assert.True(CrashRecoveryOutcomePolicy.ShouldClearPending(retryRecovery));
        Assert.True(CrashRecoveryOutcomePolicy.ShouldStartMonitor(CrashRecoveryOutcome.Success, enabled: true));
    }

    [Fact]
    public void OneFailedMonitorMustNotPreventAnotherSuccessfulMonitorFromStarting()
    {
        Assert.True(CrashRecoveryOutcomePolicy.ShouldStartMonitor(CrashRecoveryOutcome.Success, enabled: true));
        Assert.False(CrashRecoveryOutcomePolicy.ShouldStartMonitor(CrashRecoveryOutcome.SendFailed, enabled: true));
    }

    [Fact]
    public void RecoveryBatchNeverClearsPendingWhenAnyMonitorStillFailed()
    {
        Assert.False(CrashRecoveryOutcomePolicy.ShouldClearPending(new[]
        {
            CrashRecoveryOutcome.Success,
            CrashRecoveryOutcome.SendFailed,
            CrashRecoveryOutcome.Success
        }));
    }
}

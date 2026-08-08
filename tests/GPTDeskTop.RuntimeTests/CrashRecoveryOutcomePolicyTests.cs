using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoveryOutcomePolicyTests
{
    [Fact]
    public void FailedRecoveryDoesNotStartEnabledMonitor()
    {
        Assert.False(CrashRecoveryOutcomePolicy.ShouldStartMonitor(CrashRecoveryOutcome.SendFailed, enabled: true));
    }

    [Fact]
    public void SuccessfulRecoveryStartsEnabledMonitor()
    {
        Assert.True(CrashRecoveryOutcomePolicy.ShouldStartMonitor(CrashRecoveryOutcome.Success, enabled: true));
    }

    [Fact]
    public void DisabledMonitorNeverStartsEvenAfterSuccessfulRecovery()
    {
        Assert.False(CrashRecoveryOutcomePolicy.ShouldStartMonitor(CrashRecoveryOutcome.Success, enabled: false));
    }

    [Fact]
    public void PendingRemainsWhenAnyMonitorRecoveryFails()
    {
        Assert.False(CrashRecoveryOutcomePolicy.ShouldClearPending([
            CrashRecoveryOutcome.Success,
            CrashRecoveryOutcome.SendFailed
        ]));
    }

    [Fact]
    public void PendingClearsOnlyWhenAllRecoveriesSucceed()
    {
        Assert.True(CrashRecoveryOutcomePolicy.ShouldClearPending([
            CrashRecoveryOutcome.Success,
            CrashRecoveryOutcome.Success
        ]));
    }

    [Fact]
    public void EmptyRecoveryBatchDoesNotClearPending()
    {
        Assert.False(CrashRecoveryOutcomePolicy.ShouldClearPending([]));
    }
}

using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CdpRecoveryResumeRegressionTests
{
    [Fact]
    public void MonitorPollUsesRecoveryAwareStateReadInsteadOfRawCdpRead()
    {
        var source = MonitorSource();
        var loop = Slice(source, "private async Task MonitorLoopAsync", "private async Task<bool> ConfirmFreshChatGenerationBoundaryAsync");
        Assert.Contains("var state = await GetChatStateWithRetryAsync(monitor.Id, tab, cancellationToken);", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("var state = await _chrome.GetChatStateAsync(tab, cancellationToken);", loop, StringComparison.Ordinal);
    }

    [Fact]
    public void StateReadIsBoundedSoRecoveredTransportCannotHangTheMonitorForever()
    {
        var source = MonitorSource();
        Assert.Contains("MonitorChatStateReadTimeout = TimeSpan.FromSeconds(12)", source, StringComparison.Ordinal);
        var bounded = Slice(source, "private async Task<ChatPageState> ReadChatStateBoundedAsync", "private static bool IsTransientChromeException");
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)", bounded, StringComparison.Ordinal);
        Assert.Contains("CancelAfter(MonitorChatStateReadTimeout)", bounded, StringComparison.Ordinal);
        Assert.Contains("throw new TimeoutException", bounded, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulRebindRequiresAnAuthoritativeReadBeforeRecoveryIsDeclaredResumed()
    {
        var source = MonitorSource();
        var retry = Slice(source, "private async Task<ChatPageState> GetChatStateWithRetryAsync", "private async Task<ChatPageState> ReadChatStateBoundedAsync");
        var recovery = retry.IndexOf("Chrome/CDP recovery complete: same conversation target is stable.", StringComparison.Ordinal);
        var boundedRead = retry.IndexOf("await ReadChatStateBoundedAsync(tab, cancellationToken)", StringComparison.Ordinal);
        var resumed = retry.IndexOf("Chrome/CDP recovery read verified; monitor polling resumed.", StringComparison.Ordinal);
        Assert.True(recovery >= 0 && boundedRead >= 0 && resumed >= 0);
        Assert.Contains("recoveredTransport = true", retry, StringComparison.Ordinal);
        Assert.Contains("TransportRecoveryResume", retry, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryFixDoesNotRelaxExactlyOnceOrGlobalDwellInvariants()
    {
        var source = MonitorSource();
        Assert.Contains("MinimumStableSendDwell = TimeSpan.FromSeconds(15)", source, StringComparison.Ordinal);
        Assert.Contains("Verified message accepted. Exactly-once guard closed the delivery operation.", source, StringComparison.Ordinal);
        Assert.Contains("Exactly-once guard suppressed blind resend", source, StringComparison.Ordinal);
        Assert.Contains("ContinueInFreshChatAfterResponseAsync", source, StringComparison.Ordinal);
    }

    private static string MonitorSource() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}

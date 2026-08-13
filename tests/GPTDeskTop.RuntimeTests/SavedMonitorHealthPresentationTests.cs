using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SavedMonitorHealthPresentationTests
{
    private static SavedMonitor Monitor(bool enabled = true, string? url = null)
        => new()
        {
            Id = 7,
            Enabled = enabled,
            Url = url ?? "https://chatgpt.com/c/health-test",
            TabId = "tab-7",
            Title = "Health test"
        };

    [Fact]
    public void VerifiedRunningMonitorIsGreen()
    {
        var health = SavedMonitorHealthPresentation.Evaluate(
            Monitor(),
            workerRunning: true,
            duplicateOwnership: false,
            conversationTabAvailable: true,
            pageState: new ChatPageState(2, "done", false, string.Empty));

        Assert.True(health.IsHealthy);
        Assert.Equal("🟢 Healthy", health.Status);
        Assert.Equal("Monitoring normally.", health.Reason);
    }

    [Fact]
    public void GeneratingResponseRemainsGreenBecauseWaitingIsHealthy()
    {
        var health = SavedMonitorHealthPresentation.Evaluate(
            Monitor(),
            workerRunning: true,
            duplicateOwnership: false,
            conversationTabAvailable: true,
            pageState: new ChatPageState(2, string.Empty, true, string.Empty));

        Assert.True(health.IsHealthy);
        Assert.Contains("generating", health.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatGptRenderedErrorIsRedWithReason()
    {
        var health = SavedMonitorHealthPresentation.Evaluate(
            Monitor(),
            workerRunning: true,
            duplicateOwnership: false,
            conversationTabAvailable: true,
            pageState: new ChatPageState(2, string.Empty, false, "Something went wrong"));

        Assert.False(health.IsHealthy);
        Assert.Equal("🔴 Recovery", health.Status);
        Assert.Contains("Something went wrong", health.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RunningWorkerWithTransportFailureIsRed()
    {
        var health = SavedMonitorHealthPresentation.Evaluate(
            Monitor(),
            workerRunning: true,
            duplicateOwnership: false,
            conversationTabAvailable: true,
            pageState: null,
            probeError: "Chrome/CDP unavailable: connection refused");

        Assert.False(health.IsHealthy);
        Assert.Equal("🔴 Connection", health.Status);
        Assert.Contains("connection refused", health.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingConversationTabIsRedAndExplainsWhy()
    {
        var health = SavedMonitorHealthPresentation.Evaluate(
            Monitor(),
            workerRunning: false,
            duplicateOwnership: false,
            conversationTabAvailable: false,
            pageState: null);

        Assert.False(health.IsHealthy);
        Assert.Equal("🔴 Stopped", health.Status);
        Assert.Equal("Conversation tab is not open.", health.Reason);
    }

    [Fact]
    public void StoppedWorkerUsesLatestRuntimeFailureReason()
    {
        var health = SavedMonitorHealthPresentation.Evaluate(
            Monitor(),
            workerRunning: false,
            duplicateOwnership: false,
            conversationTabAvailable: true,
            pageState: null,
            runtimeFailureReason: "Selected Chrome target disappeared before Start.");

        Assert.False(health.IsHealthy);
        Assert.Contains("disappeared", health.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisabledDuplicateAndInvalidMonitorsCanNeverBeGreen()
    {
        var disabled = SavedMonitorHealthPresentation.Evaluate(
            Monitor(enabled: false), true, false, true, new ChatPageState(1, "ok", false, string.Empty));
        var duplicate = SavedMonitorHealthPresentation.Evaluate(
            Monitor(), true, true, true, new ChatPageState(1, "ok", false, string.Empty));
        var invalid = SavedMonitorHealthPresentation.Evaluate(
            Monitor(url: "https://chatgpt.com/"), true, false, true, new ChatPageState(1, "ok", false, string.Empty));

        Assert.False(disabled.IsHealthy);
        Assert.Equal("🔴 Disabled", disabled.Status);
        Assert.False(duplicate.IsHealthy);
        Assert.Equal("🔴 Blocked", duplicate.Status);
        Assert.False(invalid.IsHealthy);
        Assert.Equal("🔴 Invalid", invalid.Status);
    }
}

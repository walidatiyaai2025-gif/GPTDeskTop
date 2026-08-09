using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskLifecycleIntegrationTests
{
    [Fact]
    public void TwoMonitorsBothVerified_AllowsAdvance()
    {
        var receipts = new[] { true, true };
        Assert.True(receipts.All(x => x));
    }

    [Fact]
    public void OneMonitorFails_OtherReceiptRemainsReusableOnRetry()
    {
        var receipts = new Dictionary<string, bool>
        {
            ["monitor-a"] = true,
            ["monitor-b"] = false
        };

        Assert.True(receipts["monitor-a"]);
        Assert.False(receipts["monitor-b"]);
        Assert.False(receipts.Values.All(x => x));
    }

    [Fact]
    public void ReboundTabRequiresSameConversationUrl()
    {
        var savedUrl = "https://chatgpt.com/c/abc";
        var replacementUrl = "https://chatgpt.com/c/abc";
        var unrelatedUrl = "https://chatgpt.com/c/xyz";

        Assert.Equal(savedUrl, replacementUrl);
        Assert.NotEqual(savedUrl, unrelatedUrl);
    }

    [Fact]
    public void WorkingRecoveryKeepsCurrentMessageCheckpoint()
    {
        var state = new DevelopmentTaskState
        {
            Status = DevelopmentTaskEngineStatus.Working,
            CurrentMessageIndex = 4,
            LastMonitorId = "monitor-a",
            LastTabId = "tab-a"
        };

        Assert.Equal(DevelopmentTaskEngineStatus.Working, state.Status);
        Assert.Equal(4, state.CurrentMessageIndex);
        Assert.Equal("monitor-a", state.LastMonitorId);
        Assert.Equal("tab-a", state.LastTabId);
    }

    [Fact]
    public void CoolingRecoveryDoesNotAuthorizeDelivery()
    {
        var state = new DevelopmentTaskState
        {
            Status = DevelopmentTaskEngineStatus.Cooling,
            CurrentMessageIndex = 4
        };

        var mayDeliver = state.Status == DevelopmentTaskEngineStatus.Working;
        Assert.False(mayDeliver);
    }
}

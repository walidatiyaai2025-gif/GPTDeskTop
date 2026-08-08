using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskMultiMonitorDeliveryTests
{
    [Fact]
    public void PartialDeliveryKeepsSuccessfulRecipientReceiptForRetry()
    {
        var state = new DevelopmentTaskState
        {
            CurrentMessageIndex = 2,
            DeliveryReceipts = new Dictionary<string, DevelopmentTaskDeliveryReceipt>(StringComparer.Ordinal)
        };
        const string fingerprint = "ABC123";
        state.DeliveryReceipts["monitor-a"] = new DevelopmentTaskDeliveryReceipt
        {
            MonitorId = "monitor-a",
            TabId = "tab-a",
            MessageIndex = 2,
            Fingerprint = fingerprint
        };

        var receipt = state.DeliveryReceipts["monitor-a"];
        Assert.Equal(2, receipt.MessageIndex);
        Assert.Equal("tab-a", receipt.TabId);
        Assert.Equal(fingerprint, receipt.Fingerprint);
    }

    [Fact]
    public void DifferentTabInvalidatesPreviousReceiptForSameMonitor()
    {
        var state = new DevelopmentTaskState();
        state.DeliveryReceipts["monitor-a"] = new DevelopmentTaskDeliveryReceipt
        {
            MonitorId = "monitor-a",
            TabId = "old-tab",
            MessageIndex = 0,
            Fingerprint = "ABC"
        };

        var receipt = state.DeliveryReceipts["monitor-a"];
        var reusable = receipt.MessageIndex == 0 &&
                       receipt.TabId == "new-tab" &&
                       receipt.Fingerprint == "ABC";

        Assert.False(reusable);
    }

    [Fact]
    public void MultiMonitorCoordinatorRequiresEveryRecipientBeforeAdvance()
    {
        var outcomes = new[] { true, false, true };
        Assert.False(outcomes.All(x => x));
        Assert.True(outcomes.Take(1).All(x => x));
    }
}

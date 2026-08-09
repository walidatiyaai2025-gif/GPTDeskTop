using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorConversationOwnershipTests
{
    [Fact]
    public void DuplicateStableConversationOwnersAreAllQuarantinedCaseInsensitively()
    {
        var monitors = new[]
        {
            Monitor(1, "https://chatgpt.com/c/shared-owner"),
            Monitor(2, "https://CHATGPT.com/c/SHARED-owner"),
            Monitor(3, "https://chatgpt.com/c/unique-owner"),
            Monitor(4, "https://chatgpt.com/")
        };

        var duplicateIds = MonitorConversationOwnership.FindDuplicateMonitorIds(monitors);

        Assert.Equal(2, duplicateIds.Count);
        Assert.Contains(1, duplicateIds);
        Assert.Contains(2, duplicateIds);
        Assert.DoesNotContain(3, duplicateIds);
        Assert.DoesNotContain(4, duplicateIds);
        Assert.Equal(2, MonitorConversationOwnership.CountDuplicateMonitors(monitors));
        Assert.True(MonitorConversationOwnership.IsDuplicateOwner(1, monitors));
        Assert.False(MonitorConversationOwnership.IsDuplicateOwner(3, monitors));
    }

    [Fact]
    public void ThreeWayDuplicateQuarantinesEveryOwnerAndIgnoresInvalidIdentityDuplicates()
    {
        var monitors = new[]
        {
            Monitor(10, "https://chatgpt.com/c/three-way"),
            Monitor(11, "https://chatgpt.com/c/THREE-way"),
            Monitor(12, "https://chatgpt.com/c/three-WAY"),
            Monitor(13, "https://chatgpt.com/"),
            Monitor(14, "https://chatgpt.com/")
        };

        var duplicateIds = MonitorConversationOwnership.FindDuplicateMonitorIds(monitors);

        Assert.Equal(new long[] { 10, 11, 12 }, duplicateIds.OrderBy(id => id).ToArray());
    }

    [Fact]
    public void UniqueStableOwnersProduceNoQuarantine()
    {
        var monitors = new[]
        {
            Monitor(21, "https://chatgpt.com/c/one"),
            Monitor(22, "https://chatgpt.com/c/two")
        };

        Assert.Empty(MonitorConversationOwnership.FindDuplicateMonitorIds(monitors));
    }

    private static SavedMonitor Monitor(long id, string url)
        => new() { Id = id, TabId = $"tab-{id}", Title = $"Monitor {id}", Url = url, Enabled = true };
}
using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services.DevelopmentTaskEngine;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskMonitorTargetFactoryTests
{
    [Fact]
    public void UrlRebindingProducesUpdatedPersistedTargetIdentity()
    {
        var monitor = new SavedMonitor
        {
            Id = 7,
            TabId = "old-target",
            Url = "https://chatgpt.com/c/abc/"
        };
        var replacement = new ChromeTab
        {
            Id = "new-target",
            Url = "https://chatgpt.com/c/abc"
        };

        var result = SavedMonitorTabResolver.Resolve(monitor, new[] { replacement });

        Assert.True(result.Found);
        Assert.Equal("PersistedConversationUrl", result.MatchType);
        Assert.Equal("new-target", result.Tab!.Id);
        Assert.NotEqual(monitor.TabId, result.Tab.Id);
    }

    [Fact]
    public void MissingConversationDoesNotResolveByTitle()
    {
        var monitor = new SavedMonitor
        {
            Id = 8,
            TabId = "missing",
            Title = "CARGame",
            Url = "https://chatgpt.com/c/expected"
        };
        var unrelated = new ChromeTab
        {
            Id = "other",
            Title = "CARGame",
            Url = "https://chatgpt.com/c/other"
        };

        var result = SavedMonitorTabResolver.Resolve(monitor, new[] { unrelated });

        Assert.False(result.Found);
        Assert.Null(result.Tab);
        Assert.Equal("None", result.MatchType);
    }
}

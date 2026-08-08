using GPTDeskTop.Models;
using GPTDeskTop.Services;
using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class SavedMonitorTabResolverTests
{
    [Fact]
    public void ExactPersistedTabIdWins()
    {
        var monitor = new SavedMonitor { TabId = "tab-2", Url = "https://chatgpt.com/c/conversation-2" };
        var tabs = new[]
        {
            new ChromeTab { Id = "tab-1", Url = "https://chatgpt.com/c/conversation-2" },
            new ChromeTab { Id = "tab-2", Url = "https://chatgpt.com/c/other" }
        };

        var result = SavedMonitorTabResolver.Resolve(monitor, tabs);

        Assert.True(result.Found);
        Assert.Equal("tab-2", result.Tab!.Id);
        Assert.Equal("PersistedTabId", result.MatchType);
    }

    [Fact]
    public void RecreatedChromeTargetCanBeRecoveredByConversationUrl()
    {
        var monitor = new SavedMonitor { TabId = "old-target", Url = "https://chatgpt.com/c/conversation-2/" };
        var tabs = new[]
        {
            new ChromeTab { Id = "new-target", Url = "https://chatgpt.com/c/conversation-2" }
        };

        var result = SavedMonitorTabResolver.Resolve(monitor, tabs);

        Assert.True(result.Found);
        Assert.Equal("new-target", result.Tab!.Id);
        Assert.Equal("PersistedConversationUrl", result.MatchType);
    }

    [Fact]
    public void MissingConversationDoesNotFallBackToTitle()
    {
        var monitor = new SavedMonitor { TabId = "missing", Url = "https://chatgpt.com/c/conversation-2", Title = "My Chat" };
        var tabs = new[]
        {
            new ChromeTab { Id = "different", Url = "https://chatgpt.com/c/conversation-3", Title = "My Chat" }
        };

        var result = SavedMonitorTabResolver.Resolve(monitor, tabs);

        Assert.False(result.Found);
        Assert.Null(result.Tab);
    }
}

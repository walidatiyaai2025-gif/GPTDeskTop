using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class NewChatStableTargetSelectorTests
{
    [Fact]
    public void SameTargetBecomingStableWins()
    {
        var opened = Tab("new-target", "https://chatgpt.com/");
        var baseline = new HashSet<string>(StringComparer.Ordinal) { "old-target" };
        var stable = Tab("new-target", "https://chatgpt.com/c/new-conversation");

        var selected = NewChatStableTargetSelector.Select(opened, baseline, new[]
        {
            Tab("old-target", "https://chatgpt.com/c/old-conversation"),
            stable
        });

        Assert.Same(stable, selected);
    }

    [Fact]
    public void ReplacedTargetIsRecoveredWhenItIsTheOnlyNewStableConversation()
    {
        var opened = Tab("transient-target", "https://chatgpt.com/");
        var baseline = new HashSet<string>(StringComparer.Ordinal) { "old-a", "old-b" };
        var replacement = Tab("replacement-target", "https://chatgpt.com/c/new-conversation");

        var selected = NewChatStableTargetSelector.Select(opened, baseline, new[]
        {
            Tab("old-a", "https://chatgpt.com/c/old-a"),
            Tab("old-b", "https://chatgpt.com/c/old-b"),
            replacement
        });

        Assert.Same(replacement, selected);
    }

    [Fact]
    public void OldConversationIsNeverMistakenForReplacement()
    {
        var opened = Tab("transient-target", "https://chatgpt.com/");
        var baseline = new HashSet<string>(StringComparer.Ordinal) { "old-target" };

        var selected = NewChatStableTargetSelector.Select(opened, baseline, new[]
        {
            Tab("old-target", "https://chatgpt.com/c/old-conversation")
        });

        Assert.Null(selected);
    }

    [Fact]
    public void MultipleNewStableTargetsFailClosed()
    {
        var opened = Tab("transient-target", "https://chatgpt.com/");
        var baseline = new HashSet<string>(StringComparer.Ordinal) { "old-target" };

        var selected = NewChatStableTargetSelector.Select(opened, baseline, new[]
        {
            Tab("old-target", "https://chatgpt.com/c/old-conversation"),
            Tab("new-a", "https://chatgpt.com/c/new-a"),
            Tab("new-b", "https://chatgpt.com/c/new-b")
        });

        Assert.Null(selected);
    }

    private static ChromeTab Tab(string id, string url)
        => new()
        {
            Id = id,
            Url = url,
            Title = id,
            Type = "page",
            WebSocketDebuggerUrl = $"ws://localhost/{id}"
        };
}

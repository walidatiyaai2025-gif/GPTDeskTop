using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ChatGptConversationIdentityQueryRegressionTests
{
    [Fact]
    public void QueryOnlyNavigationPreservesDurableConversationIdentity()
    {
        const string left = "https://chatgpt.com/c/abc-123?model=auto";
        const string right = "https://chatgpt.com/c/abc-123?temporary-chat=false";

        Assert.True(ChatGptConversationIdentity.IsSame(left, right));
        Assert.Equal("https://chatgpt.com/c/abc-123", ChatGptConversationIdentity.Normalize(left));
    }

    [Fact]
    public void DifferentConversationIdStillFailsClosed()
    {
        Assert.False(ChatGptConversationIdentity.IsSame(
            "https://chatgpt.com/c/abc-123?model=auto",
            "https://chatgpt.com/c/other-456?model=auto"));
    }
}

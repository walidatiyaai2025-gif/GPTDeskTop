using GPTDeskTop.Runtime;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

public sealed class ChatGptRuntimeStateEngineTests
{
    [Fact]
    public void CanonicalPolicyMatrix_CoversEveryDeclaredRuntimeStateAndFailsClosed()
    {
        var states = Enum.GetValues<ChatGptRuntimeState>();
        Assert.Equal(29, states.Length);
        foreach (var state in states)
        {
            var policy = ChatGptRuntimeStateEngine.PolicyForState(state);
            Assert.Equal(state, policy.State);
            Assert.False(string.IsNullOrWhiteSpace(policy.ResumeCondition));
            if (state is not (ChatGptRuntimeState.Ready or ChatGptRuntimeState.ResponseComplete))
                Assert.NotEqual(RuntimeSendPolicy.Allow, policy.SendPolicy);
        }
    }

    [Theory]
    [InlineData("Too many requests", ChatGptRuntimeState.TooManyRequests, RuntimeStateScope.Global)]
    [InlineData("Log in to ChatGPT", ChatGptRuntimeState.LoginRequired, RuntimeStateScope.Global)]
    [InlineData("Verify you are human", ChatGptRuntimeState.SecurityChallenge, RuntimeStateScope.Global)]
    [InlineData("usage limit reached", ChatGptRuntimeState.UsageLimit, RuntimeStateScope.Global)]
    [InlineData("upgrade your plan", ChatGptRuntimeState.PlanLimit, RuntimeStateScope.Global)]
    [InlineData("network error", ChatGptRuntimeState.NetworkError, RuntimeStateScope.Global)]
    [InlineData("maximum context length", ChatGptRuntimeState.ContextLimit, RuntimeStateScope.Monitor)]
    [InlineData("conversation not found", ChatGptRuntimeState.ConversationNotFound, RuntimeStateScope.Monitor)]
    [InlineData("model is unavailable", ChatGptRuntimeState.ModelUnavailable, RuntimeStateScope.Monitor)]
    [InlineData("something went wrong", ChatGptRuntimeState.SomethingWentWrong, RuntimeStateScope.Monitor)]
    public void CurrentVisibleBlockingEvidence_ClassifiesWithCanonicalPolicy(string text, ChatGptRuntimeState state, RuntimeStateScope scope)
    {
        var decision = ChatGptRuntimeStateEngine.Classify(new(CurrentBlockingText: text));
        Assert.Equal(state, decision.State);
        Assert.Equal(scope, decision.Scope);
        Assert.Equal(RuntimeSendPolicy.Block, decision.SendPolicy);
    }

    [Fact]
    public void UnknownWrongChatAndSecurityStates_FailClosed()
    {
        Assert.Equal(RuntimeSendPolicy.Block, ChatGptRuntimeStateEngine.Classify(new(UnknownBlockingUi: true)).SendPolicy);
        Assert.Equal(ChatGptRuntimeState.WrongChat, ChatGptRuntimeStateEngine.Classify(new(WrongChat: true)).State);
        Assert.Equal(RuntimeSendPolicy.Block, ChatGptRuntimeStateEngine.Classify(new(CurrentBlockingText: "security check")).SendPolicy);
    }

    [Fact]
    public void HistoricalAssistantText_IsNotAnInputAndCannotCreateFalseCurrentError()
    {
        var decision = ChatGptRuntimeStateEngine.Classify(new(ResponseCompleted: true, CurrentBlockingText: ""));
        Assert.Equal(ChatGptRuntimeState.ResponseComplete, decision.State);
        Assert.Equal(RuntimeSendPolicy.Allow, decision.SendPolicy);
    }

    [Theory]
    [InlineData(true, false, false, ChatGptRuntimeState.PageLoading)]
    [InlineData(false, true, false, ChatGptRuntimeState.Generating)]
    [InlineData(false, false, true, ChatGptRuntimeState.SendUncertain)]
    public void StructuredTransientStates_BlockOrWait(bool loading, bool generating, bool uncertain, ChatGptRuntimeState expected)
    {
        var decision = ChatGptRuntimeStateEngine.Classify(new(PageLoading: loading, IsGenerating: generating, SendUncertain: uncertain));
        Assert.Equal(expected, decision.State);
        Assert.NotEqual(RuntimeSendPolicy.Allow, decision.SendPolicy);
    }
}

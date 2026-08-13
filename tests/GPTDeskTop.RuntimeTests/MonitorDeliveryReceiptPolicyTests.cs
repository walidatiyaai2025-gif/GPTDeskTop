using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorDeliveryReceiptPolicyTests
{
    [Fact]
    public void CompletedAssistantTurn_DoesNotReuseRepeatedUserText()
    {
        Assert.False(MonitorDeliveryRecoveryPolicy.CanReuseMatchingUserTailAsReceipt(
            requireNewTurn: false,
            userMessageCount: 7,
            assistantMessageCount: 7,
            isGenerating: false));
    }

    [Fact]
    public void ActiveOrUnansweredUserTurn_CanBeReusedAsReceipt()
    {
        Assert.True(MonitorDeliveryRecoveryPolicy.CanReuseMatchingUserTailAsReceipt(
            requireNewTurn: false,
            userMessageCount: 7,
            assistantMessageCount: 6,
            isGenerating: false));

        Assert.True(MonitorDeliveryRecoveryPolicy.CanReuseMatchingUserTailAsReceipt(
            requireNewTurn: false,
            userMessageCount: 7,
            assistantMessageCount: 7,
            isGenerating: true));
    }

    [Fact]
    public void ExplicitNewTurn_NeverReusesMatchingTail()
    {
        Assert.False(MonitorDeliveryRecoveryPolicy.CanReuseMatchingUserTailAsReceipt(
            requireNewTurn: true,
            userMessageCount: 7,
            assistantMessageCount: 6,
            isGenerating: true));
    }
}

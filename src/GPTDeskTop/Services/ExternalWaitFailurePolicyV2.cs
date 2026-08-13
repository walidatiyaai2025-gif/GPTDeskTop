namespace GPTDeskTop.Services;
public enum ExternalWaitFailureActionV2 { RetryLater, BlockTask, RequestHumanReview, FailTask }
public static class ExternalWaitFailurePolicyV2
{
    public static ExternalWaitFailureActionV2 Decide(ExternalWaitStatus status, bool retryable, bool humanResolvable) => status switch
    {
        ExternalWaitStatus.TimedOut when retryable => ExternalWaitFailureActionV2.RetryLater,
        ExternalWaitStatus.Failed when humanResolvable => ExternalWaitFailureActionV2.RequestHumanReview,
        ExternalWaitStatus.Failed => ExternalWaitFailureActionV2.FailTask,
        _ => ExternalWaitFailureActionV2.BlockTask
    };
}

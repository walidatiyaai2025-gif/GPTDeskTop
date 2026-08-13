namespace GPTDeskTop.Services;

public enum PostModalResumeAction { ResumeMonitoring, RetryDismiss, WaitForHuman, IgnoreUnknown }

public static class PostModalResumePolicy
{
    public static PostModalResumeAction Decide(DismissibleModalResult result, ModalDismissVerification verification) => result switch
    {
        DismissibleModalResult.HumanVerificationDetected => PostModalResumeAction.WaitForHuman,
        DismissibleModalResult.Dismissed when verification.CanResume => PostModalResumeAction.ResumeMonitoring,
        DismissibleModalResult.Dismissed or DismissibleModalResult.StillVisible => PostModalResumeAction.RetryDismiss,
        _ => PostModalResumeAction.IgnoreUnknown
    };
}

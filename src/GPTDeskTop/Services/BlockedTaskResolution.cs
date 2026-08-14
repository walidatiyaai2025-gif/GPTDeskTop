namespace GPTDeskTop.Services;
public enum BlockedTaskResolution { Wait, Retry, RequestHumanAction, WaitExternal, RequestApproval, RebaseOrResolveConflict, ManualReview }
public static class BlockedTaskResolutionPolicy
{
    public static BlockedTaskResolution For(BlockedTaskReason reason) => reason switch
    {
        BlockedTaskReason.HumanActionRequired => BlockedTaskResolution.RequestHumanAction,
        BlockedTaskReason.ExternalDependency => BlockedTaskResolution.WaitExternal,
        BlockedTaskReason.ApprovalRequired => BlockedTaskResolution.RequestApproval,
        BlockedTaskReason.RepositoryConflict => BlockedTaskResolution.RebaseOrResolveConflict,
        BlockedTaskReason.VerificationFailed => BlockedTaskResolution.Retry,
        _ => BlockedTaskResolution.ManualReview
    };
}

namespace GPTDeskTop.Services;
public enum ChatRotationStage
{
    NotStarted,
    CheckpointSaved,
    NewChatCreated,
    ContinuationSent,
    ContinuationVerified,
    OldChatDeleted,
    Completed,
    Failed
}

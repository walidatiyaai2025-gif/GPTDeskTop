namespace GPTDeskTop.Services;
public enum ChatRotationReason
{
    ContextLimit,
    RepeatedStall,
    RepeatedModelDelay,
    ToolLoop,
    CorruptedConversation,
    UserRequested
}

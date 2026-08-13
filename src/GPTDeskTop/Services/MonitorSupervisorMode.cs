namespace GPTDeskTop.Services;
public enum MonitorSupervisorMode
{
    Observing,
    Sending,
    WaitingForReply,
    WaitingForHuman,
    Recovering,
    RotatingChat,
    Paused,
    Completed
}

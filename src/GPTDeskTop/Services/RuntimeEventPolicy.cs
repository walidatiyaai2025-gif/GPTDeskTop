namespace GPTDeskTop.Services;
public static class RuntimeEventPolicy
{
    public static bool RefreshDashboard(RuntimeEventKind kind) => kind is RuntimeEventKind.TaskChanged or RuntimeEventKind.ExternalWaitStarted or RuntimeEventKind.ExternalWaitEnded or RuntimeEventKind.RecoveryStarted or RuntimeEventKind.RecoveryEnded or RuntimeEventKind.ChatRotated or RuntimeEventKind.ProjectCompleted;
}

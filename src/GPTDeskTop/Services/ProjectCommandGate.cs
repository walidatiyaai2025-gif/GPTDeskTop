namespace GPTDeskTop.Services;
public static class ProjectCommandGate
{
    public static bool Allows(ProjectRunCommand command, ProjectRuntimeStatus status) => command switch
    {
        ProjectRunCommand.Start => status == ProjectRuntimeStatus.Idle,
        ProjectRunCommand.Pause => status is ProjectRuntimeStatus.Running or ProjectRuntimeStatus.WaitingForReply,
        ProjectRunCommand.Resume => status == ProjectRuntimeStatus.Idle,
        ProjectRunCommand.Stop => status != ProjectRuntimeStatus.Completed,
        ProjectRunCommand.RetryCurrentTask => status is ProjectRuntimeStatus.Blocked or ProjectRuntimeStatus.Recovering,
        _ => false
    };
}

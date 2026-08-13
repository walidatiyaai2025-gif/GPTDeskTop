namespace GPTDeskTop.Services;
public enum DependencyResumeDecision { KeepWaiting, Resume, Block, RequireHuman }
public static class DependencyResumeDecisionPolicy
{
    public static DependencyResumeDecision Decide(ProjectRuntimeStatus status, bool dependencySatisfied, bool needsHuman)
    {
        if (needsHuman) return DependencyResumeDecision.RequireHuman;
        if (dependencySatisfied) return DependencyResumeDecision.Resume;
        if (status == ProjectRuntimeStatus.Blocked) return DependencyResumeDecision.Block;
        return DependencyResumeDecision.KeepWaiting;
    }
}

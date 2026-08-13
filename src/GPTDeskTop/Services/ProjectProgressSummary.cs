namespace GPTDeskTop.Services;
public sealed record ProjectProgressSummary(string ProjectId, int TotalTasks, int CompletedTasks, int RunningTasks, int BlockedTasks, int WaitingTasks)
{
    public int RemainingTasks => Math.Max(0, TotalTasks - CompletedTasks);
    public double CompletionPercent => TotalTasks == 0 ? 0 : (double)CompletedTasks / TotalTasks * 100d;
}

namespace GPTDeskTop.Services;
public sealed record GitHubCheckSummary(int Total, int Pending, int Succeeded, int Failed, int Cancelled)
{
    public bool AllSucceeded => Total > 0 && Succeeded == Total;
    public bool HasFailure => Failed > 0;
    public bool IsPending => Pending > 0;
}

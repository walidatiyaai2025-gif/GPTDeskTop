namespace GPTDeskTop.Services;
public sealed record ReleaseReadinessReport(IReadOnlyList<ProductionReadinessCheck> Checks, DateTimeOffset GeneratedAt)
{
    public bool Ready => Checks.Where(x => x.Required).All(x => x.Passed);
    public int RequiredFailures => Checks.Count(x => x.Required && !x.Passed);
}

namespace GPTDeskTop.Services;
public static class SmokeReleaseGate
{
    public static bool Passed(IEnumerable<SmokeTestOutcome> outcomes) => outcomes.Any() && outcomes.All(x => x.Passed);
}

namespace GPTDeskTop.Services;
public enum StallStatus { Active, Warning, Suspected, Stalled }
public static class StallStatusClassifier
{
    public static StallStatus Classify(TimeSpan idle, GenerationWatchdogPolicy p) => idle >= p.HardStall ? StallStatus.Stalled : idle >= p.SuspectedStall ? StallStatus.Suspected : idle >= p.InactivityWarning ? StallStatus.Warning : StallStatus.Active;
}

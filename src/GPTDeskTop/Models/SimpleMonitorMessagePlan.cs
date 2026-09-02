namespace GPTDeskTop.Models;

public sealed class SimpleMonitorMessagePlan
{
    public int SchemaVersion { get; init; } = 1;
    public string Name { get; init; } = "Message Plan";
    public bool Loop { get; init; } = true;
    public int DefaultDelaySeconds { get; init; } = 15;
    public List<SimpleMonitorMessageStep> Messages { get; init; } = [];
}

public sealed class SimpleMonitorMessageStep
{
    public string? Label { get; init; }
    public string Text { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public int? DelaySeconds { get; init; }

    public int EffectiveDelaySeconds(int defaultDelaySeconds)
        => DelaySeconds ?? defaultDelaySeconds;
}

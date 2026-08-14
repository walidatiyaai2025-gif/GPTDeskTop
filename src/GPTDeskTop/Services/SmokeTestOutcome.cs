namespace GPTDeskTop.Services;
public sealed record SmokeTestOutcome(string Scenario, bool Passed, DateTimeOffset FinishedAt, string Detail);

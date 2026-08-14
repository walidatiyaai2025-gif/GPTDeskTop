namespace GPTDeskTop.Services;
public sealed record ValidationCheckResult(string Name, bool Passed, TimeSpan Duration, string Detail);

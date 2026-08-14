namespace GPTDeskTop.Services;
public sealed record RecoveryDecision(string ProjectId, RuntimeIntegrationState State, RecoveryAction Action, string Reason, DateTimeOffset At);

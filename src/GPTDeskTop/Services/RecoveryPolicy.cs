namespace GPTDeskTop.Services;
public static class RecoveryPolicy
{
    public static RecoveryAction Decide(RuntimeIntegrationState state, bool chatResponsive, bool githubResponsive, int attempts) => state switch
    {
        RuntimeIntegrationState.Connected when chatResponsive && githubResponsive => RecoveryAction.None,
        _ when attempts >= 3 => RecoveryAction.RequireHuman,
        _ when !chatResponsive && attempts == 0 => RecoveryAction.RefreshTab,
        _ when !chatResponsive => RecoveryAction.ReopenChat,
        _ when !githubResponsive => RecoveryAction.ReconnectGitHub,
        RuntimeIntegrationState.Recovering => RecoveryAction.RestoreCheckpoint,
        _ => RecoveryAction.None
    };
}

namespace GPTDeskTop.Services;
public enum RecoveryChoice { UsePrimary, RestoreBackup, Quarantine }
public static class ProjectRecoveryPolicy
{
    public static RecoveryChoice Choose(bool primaryValid, bool backupValid) => primaryValid ? RecoveryChoice.UsePrimary : backupValid ? RecoveryChoice.RestoreBackup : RecoveryChoice.Quarantine;
}

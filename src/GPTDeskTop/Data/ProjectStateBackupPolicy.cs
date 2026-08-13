namespace GPTDeskTop.Data;

public static class ProjectStateBackupPolicy
{
    public static string BackupPath(string statePath) => statePath + ".bak";
    public static string TemporaryPath(string statePath) => statePath + ".tmp";
}

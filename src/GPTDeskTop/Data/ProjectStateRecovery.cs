namespace GPTDeskTop.Data;
public static class ProjectStateRecovery
{
    public static string? Select(string primaryPath)
    {
        if (File.Exists(primaryPath)) return primaryPath;
        var backup = primaryPath + ".bak";
        return File.Exists(backup) ? backup : null;
    }
}

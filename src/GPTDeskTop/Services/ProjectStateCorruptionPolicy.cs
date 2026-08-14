namespace GPTDeskTop.Services;

public static class ProjectStateCorruptionPolicy
{
    public static string GetQuarantinePath(string statePath, DateTimeOffset? now = null)
    {
        var stamp = (now ?? DateTimeOffset.UtcNow).ToString("yyyyMMddHHmmss");
        return statePath + $".corrupt.{stamp}";
    }

    public static bool IsRecoverable(Exception exception) =>
        exception is System.Text.Json.JsonException or IOException or UnauthorizedAccessException;
}

namespace GPTDeskTop.Services;

public enum RuntimeHealthLevel
{
    Healthy,
    Degraded,
    Unavailable
}

public sealed record RuntimeHealthSnapshot(
    RuntimeHealthLevel Level,
    bool ChromeReachable,
    bool DatabaseReachable,
    int ChatGptTabCount,
    int SavedMonitorCount,
    int RunningMonitorCount,
    DateTimeOffset CheckedAt,
    string Summary,
    string? ChromeError,
    string? DatabaseError);

public static class RuntimeHealthPresentation
{
    public static RuntimeHealthSnapshot Create(
        bool chromeReachable,
        bool databaseReachable,
        int chatGptTabCount,
        int savedMonitorCount,
        int runningMonitorCount,
        DateTimeOffset checkedAt,
        string? chromeError = null,
        string? databaseError = null)
    {
        chatGptTabCount = Math.Max(0, chatGptTabCount);
        savedMonitorCount = Math.Max(0, savedMonitorCount);
        runningMonitorCount = Math.Clamp(runningMonitorCount, 0, savedMonitorCount);

        RuntimeHealthLevel level;
        string summary;

        if (!chromeReachable && !databaseReachable)
        {
            level = RuntimeHealthLevel.Unavailable;
            summary = "Chrome/CDP and SQLite health probes failed.";
        }
        else if (!chromeReachable || !databaseReachable)
        {
            level = RuntimeHealthLevel.Degraded;
            summary = !chromeReachable
                ? "SQLite is reachable, but Chrome/CDP is unavailable."
                : "Chrome/CDP is reachable, but SQLite is unavailable.";
        }
        else if (savedMonitorCount > 0 && chatGptTabCount == 0)
        {
            level = RuntimeHealthLevel.Degraded;
            summary = "Services are reachable, but no ChatGPT tab is open for the saved monitors.";
        }
        else
        {
            level = RuntimeHealthLevel.Healthy;
            summary = "Chrome/CDP and SQLite are reachable.";
        }

        return new RuntimeHealthSnapshot(
            level,
            chromeReachable,
            databaseReachable,
            chatGptTabCount,
            savedMonitorCount,
            runningMonitorCount,
            checkedAt,
            summary,
            NormalizeError(chromeError),
            NormalizeError(databaseError));
    }

    public static bool IsChatGptTabUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var host = uri.Host;
        return string.Equals(host, "chatgpt.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "chat.openai.com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsChatGptConversationUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!IsChatGptTabUrl(url)) return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (!string.Equals(segments[index], "c", StringComparison.OrdinalIgnoreCase)) continue;
            var conversationId = Uri.UnescapeDataString(segments[index + 1]).Trim();
            if (!string.IsNullOrWhiteSpace(conversationId)) return true;
        }

        return false;
    }

    private static string? NormalizeError(string? error)
        => string.IsNullOrWhiteSpace(error) ? null : error.Trim();
}

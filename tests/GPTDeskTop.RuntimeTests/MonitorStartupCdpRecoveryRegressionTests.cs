using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorStartupCdpRecoveryRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void StartupCdpRecoveryDoesNotStopAfterThreeTransientFailures()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        var start = source.IndexOf(
            "private async Task<ChatPageState> GetChatStateWithRetryAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private static bool IsTransientChromeException",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var body = source[start..end];
        Assert.Contains("while (true)", body, StringComparison.Ordinal);
        Assert.Contains("attempt == 4", body, StringComparison.Ordinal);
        Assert.Contains("Monitor remains active; background recovery will continue", body, StringComparison.Ordinal);
        Assert.DoesNotContain("for (var attempt = 1; attempt <= 3", body, StringComparison.Ordinal);
        Assert.DoesNotContain("throw last ??", body, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRetriesCanReachTheChromeRecoveryThreshold()
    {
        var monitorSource = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        var chromeSource = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("MonitorRecoveryFailureThreshold = 4", chromeSource, StringComparison.Ordinal);
        Assert.Contains("while (true)", monitorSource, StringComparison.Ordinal);
        Assert.Contains("attempt == 4", monitorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorUsesCanonicalChromeTransportClassifier()
    {
        var method = typeof(ChatGptMonitorService).GetMethod(
            "IsTransientChromeException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(null, [new IOException("session was invalidated")])!);
        Assert.True((bool)method.Invoke(null, [new HttpRequestException("connection refused")])!);
        Assert.True((bool)method.Invoke(null, [new ObjectDisposedException("cdp")])!);
        Assert.False((bool)method.Invoke(null, [new InvalidOperationException("permanent application failure")])!);
    }
}

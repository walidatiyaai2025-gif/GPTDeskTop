using System.Reflection;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class RunningMonitorBrowserAutoRecoveryTests
{
    [Fact]
    public void RecoverableMonitorTransportClassifierAcceptsTransportFailuresOnly()
    {
        var method = typeof(ChromeDevToolsService).GetMethod(
            "IsRecoverableMonitorTransportException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(null, [new IOException("socket unavailable")])!);
        Assert.True((bool)method.Invoke(null, [new TimeoutException("CDP timeout")])!);
        Assert.True((bool)method.Invoke(null, [new HttpRequestException("endpoint unavailable")])!);
        Assert.True((bool)method.Invoke(null, [new InvalidOperationException("Inspected target navigated or closed")])!);
        Assert.False((bool)method.Invoke(null, [new InvalidOperationException("persistent application failure")])!);
    }

    [Fact]
    public void RebindTabMovesRunningMonitorToReplacementTargetInPlace()
    {
        var service = new ChromeDevToolsService(new HttpClient(), new ChromeConfig());
        var current = new ChromeTab
        {
            Id = "old-target",
            Title = "Old",
            Url = "https://chatgpt.com/c/recovery-test",
            Type = "page",
            WebSocketDebuggerUrl = "ws://old"
        };
        var replacement = new ChromeTab
        {
            Id = "new-target",
            Title = "Recovered",
            Url = "https://chatgpt.com/c/recovery-test",
            Type = "page",
            WebSocketDebuggerUrl = "ws://new"
        };

        var method = typeof(ChromeDevToolsService).GetMethod(
            "RebindTab",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        method!.Invoke(service, [current, replacement]);

        Assert.Equal("new-target", current.Id);
        Assert.Equal("Recovered", current.Title);
        Assert.Equal("https://chatgpt.com/c/recovery-test", current.Url);
        Assert.Equal("ws://new", current.WebSocketDebuggerUrl);
    }

    [Fact]
    public void RecoveryContractStartsAfterFourthFailureAndNeverSendsAChatMessage()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("private const int MonitorRecoveryFailureThreshold = 4;", source, StringComparison.Ordinal);
        Assert.Contains("failureCount < MonitorRecoveryFailureThreshold", source, StringComparison.Ordinal);
        Assert.Contains("RecoverMonitorTabAsync(tab, cancellationToken)", source, StringComparison.Ordinal);

        var recovery = Slice(
            source,
            "private async Task<bool> RecoverMonitorTabAsync",
            "private async Task<List<ChromeTab>?> TryGetLiveTabsAsync");

        Assert.Contains("_monitorBrowserRecoveryGate.WaitAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("TryFindConversationTabAsync(tab.Url", recovery, StringComparison.Ordinal);
        Assert.Contains("CloseAllMonitorTabsAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("LaunchMonitorChrome(tab.Url)", recovery, StringComparison.Ordinal);
        Assert.Contains("WaitForConversationTabAsync(tab.Url", recovery, StringComparison.Ordinal);
        Assert.Contains("CreateTabAsync(tab.Url", recovery, StringComparison.Ordinal);
        Assert.Contains("RebindTab(tab, replacement)", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessage", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("SendWhenReady", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryUsesStableConversationIdentityInsteadOfStaleTargetId()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        var resolver = Slice(
            source,
            "private async Task<ChromeTab?> TryFindConversationTabAsync",
            "private async Task<ChromeTab?> WaitForConversationTabAsync");

        Assert.Contains("ChatGptConversationIdentity.IsSame(conversationUrl, candidate.Url)", resolver, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptConversationUrl(candidate.Url)", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate.Id", resolver, StringComparison.Ordinal);
    }

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected source markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}

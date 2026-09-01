using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

[Collection("Generation recovery safety")]
public sealed class FreshChatGenerationLeaseHandoffRegressionTests
{
    [Fact]
    public void StoppedMonitorCannotLeaveProcessWideGenerationLeaseBehind()
    {
        var lease = new GenerationRecoveryInterlock();
        var tab = Tab("stale-target", "https://chatgpt.com/c/stale-generation");
        lease.Observe(17, tab, true);
        Assert.True(lease.HasAnyActiveLease);

        Assert.True(lease.ReleaseMonitor(17, "monitor-worker-ended"));
        Assert.False(lease.IsActive(17));
        Assert.False(lease.HasAnyActiveLease);
        Assert.False(lease.ReleaseMonitor(17, "idempotent-cleanup"));
    }

    [Fact]
    public void GenericRecoveryTargetCreationRemainsGenerationGuarded()
    {
        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var generic = Slice(chrome, "public async Task<ChromeTab> CreateTabAsync", "public Task<ChromeTab> CreateNewChatTabAsync");
        Assert.Contains("GenerationRecoveryInterlock.Shared.HasAnyActiveLease", generic, StringComparison.Ordinal);
        Assert.Contains("Creating a Chrome target is forbidden while an authoritative generation lease is active", generic, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedResponseFreshChatUsesDedicatedAdditiveTargetPathAfterGenerationBoundary()
    {
        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var monitor = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var fresh = Slice(monitor, "private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync", "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");
        var boundary = Slice(monitor, "private async Task<bool> ConfirmFreshChatGenerationBoundaryAsync", "private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync");
        var additive = Slice(chrome, "public async Task<ChromeTab> CreateNewChatTabForFreshHandoffAsync", "private async Task<ChromeTab> CreateTargetCoreAsync");

        Assert.Contains("ConfirmFreshChatGenerationBoundaryAsync", fresh, StringComparison.Ordinal);
        Assert.Contains("CreateNewChatTabForFreshHandoffAsync(monitor.Id", fresh, StringComparison.Ordinal);
        Assert.Contains("GenerationRecoveryInterlock.Shared.Observe", boundary, StringComparison.Ordinal);
        Assert.Contains("for (var confirmation = 0; confirmation < 2; confirmation++)", boundary, StringComparison.Ordinal);
        Assert.Contains("FreshChatGenerationBoundary", boundary, StringComparison.Ordinal);
        Assert.Contains("GenerationRecoveryInterlock.Shared.IsActive(monitorId)", additive, StringComparison.Ordinal);
        Assert.DoesNotContain("HasAnyActiveLease", additive, StringComparison.Ordinal);
        Assert.Contains("FreshChatTargetCreateAllowed", additive, StringComparison.Ordinal);
        Assert.Contains("CreateTargetCoreAsync(_config.StartUrl", additive, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorWorkerFinallyAlwaysClearsItsGenerationLease()
    {
        var monitor = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var loop = Slice(monitor, "private async Task MonitorLoopAsync", "private async Task<bool> ConfirmFreshChatGenerationBoundaryAsync");
        Assert.Contains("finally", loop, StringComparison.Ordinal);
        Assert.Contains("GenerationRecoveryInterlock.Shared.ReleaseMonitor(monitor.Id, \"monitor-worker-ended\")", loop, StringComparison.Ordinal);
    }

    private static ChromeTab Tab(string id, string url) => new()
    {
        Id = id,
        Title = "Generation",
        Url = url,
        Type = "page",
        WebSocketDebuggerUrl = $"ws://127.0.0.1/devtools/page/{id}"
    };

    private static string ReadSource(params string[] parts)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts))));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}

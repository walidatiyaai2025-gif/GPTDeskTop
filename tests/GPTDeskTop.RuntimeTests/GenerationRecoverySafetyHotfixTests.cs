using System.Net;
using System.Text;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;
using GPTDeskTop.Runtime;
using GPTDeskTop.Services;
using Xunit;

namespace GPTDeskTop.RuntimeTests;

[Collection("Generation recovery safety")]
public sealed class GenerationRecoverySafetyHotfixTests
{
    private static ChromeTab Tab() => new()
    {
        Id = "target-generation-1",
        Title = "Long generation",
        Url = "https://chatgpt.com/c/generation-safety",
        Type = "page",
        WebSocketDebuggerUrl = "ws://127.0.0.1/devtools/page/target-generation-1"
    };

    [Fact]
    public async Task ContinuousTwoHourGenerationNeverAuthorizesReloadCloseOrBrowserClose()
    {
        var lease = GenerationRecoveryInterlock.Shared;
        lease.ResetForTests();
        var tab = Tab();
        var start = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);
        lease.Observe(71, tab, true, start);
        lease.Observe(71, tab, true, start.AddHours(2));

        var handler = new CountingHandler();
        using var client = new HttpClient(handler);
        var chrome = new ChromeDevToolsService(client, new ChromeConfig());
        var before = RuntimeFlightRecorder.Snapshot().LastSequence;

        Assert.False(await chrome.CloseTabAsync(tab));
        await chrome.ReloadTabAsync(tab);
        await chrome.CloseAllMonitorTabsAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.CreateTabAsync("https://chatgpt.com/"));

        Assert.True(lease.IsActive(71));
        Assert.Empty(handler.Requests);
        var events = RuntimeFlightRecorder.Snapshot().Events.Where(item => item.Sequence > before).ToArray();
        Assert.Contains(events, item => item.Category == "Monitor" && item.Action == "RecoverySuppressed" && item.Reason.Contains("active-generation", StringComparison.Ordinal));
        Assert.DoesNotContain(events, item => item.Reason is "Target.createTarget" or "Browser.close" or "Page.reload");
        Assert.DoesNotContain(events, item => item.Action.Contains("PhysicalSubmit", StringComparison.Ordinal));
    }

    [Fact]
    public void StaleElapsedTimeCannotReleaseGenerationLease()
    {
        var lease = new GenerationRecoveryInterlock();
        var tab = Tab();
        var start = DateTimeOffset.UtcNow.AddHours(-24);
        lease.Observe(72, tab, true, start);

        var snapshot = lease.Snapshot(72)!;
        Assert.True(snapshot.IsGenerationActive);
        Assert.Equal(start, snapshot.LastAuthoritativeGenerationStateUtc);
        Assert.Equal(ComposerAutomationDecision.DeferWhileGenerating,
            ChatComposerInterlockPolicy.DecideBeforeEditorMutation(true, true, true, false));
    }

    [Fact]
    public void GenerationLeaseRequiresTwoFreshNonGeneratingObservations()
    {
        var lease = new GenerationRecoveryInterlock();
        var tab = Tab();
        var before = RuntimeFlightRecorder.Snapshot().LastSequence;
        lease.Observe(73, tab, true);

        Assert.True(lease.Observe(73, tab, false).IsGenerationActive);
        var released = lease.Observe(73, tab, false);

        Assert.False(released.IsGenerationActive);
        Assert.Equal(2, released.ConsecutiveNonGeneratingObservations);
        var events = RuntimeFlightRecorder.Snapshot().Events.Where(item => item.Sequence > before).ToArray();
        Assert.Contains(events, item => item.Category == "Monitor" && item.Action == "GenerationLeaseAcquired");
        Assert.Contains(events, item => item.Category == "Monitor" && item.Action == "GenerationLeaseReleased");
    }

    [Fact]
    public void PositivelyConfirmedDestroyedTargetReleasesLeaseForInfrastructureRecovery()
    {
        var lease = new GenerationRecoveryInterlock();
        var tab = Tab();
        lease.Observe(75, tab, true);

        Assert.True(lease.ConfirmTargetDestroyed(tab));
        Assert.False(lease.IsActive(75));
    }

    [Fact]
    public void TransportFailurePathReconnectsOnlySameTargetWhileLeaseIsActive()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));
        var method = Slice(source, "private async Task<ChatPageState> RetrySameTargetGenerationObservationAsync", "private string BuildChatStateInstallExpression");
        Assert.Contains("string.Equals(candidate.Id, targetId", method, StringComparison.Ordinal);
        Assert.Contains("_sessionPool.Invalidate(targetId)", method, StringComparison.Ordinal);
        Assert.Contains("TransportReconnectDeferred", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateTabAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseTabAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadTabAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseAllMonitorTabsAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMonitorDestructivePathUsesFreshGenerationGate()
    {
        var source = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        foreach (var operation in new[] { "message-count-rotation", "context-rotation", "delivery-timeout-recovery", "chatgpt-error-recovery" })
            Assert.Contains($"CanPerformDestructiveAutomationAsync(monitor.Id, {((operation == "message-count-rotation") ? "oldTab" : "tab")}, \"{operation}\"", source, StringComparison.Ordinal);
        var gate = Slice(source, "private async Task<bool> CanPerformDestructiveAutomationAsync", "private static ChatGptRuntimeEvidence");
        Assert.Contains("TryGetChatStatePassiveAsync", gate, StringComparison.Ordinal);
        Assert.Contains("fresh.IsGenerating", gate, StringComparison.Ordinal);
        Assert.Contains("GenerationRecoveryInterlock.Shared.IsActive", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderedErrorCanRecoverOnlyAfterGenerationIsAuthoritativelyFinished()
    {
        var lease = new GenerationRecoveryInterlock();
        var tab = Tab();
        lease.Observe(74, tab, true);
        lease.Observe(74, tab, false);
        Assert.True(lease.IsActive(74));
        lease.Observe(74, tab, false);
        Assert.False(lease.IsActive(74));

        var decision = ChatGptRuntimeStateEngine.Classify(new(CurrentBlockingText: "Something went wrong"));
        Assert.Equal(ChatGptRuntimeState.SomethingWentWrong, decision.State);
        Assert.Equal(RuntimeRecoveryPolicy.ReloadSameConversation, decision.RecoveryPolicy);
    }

    [Fact]
    public void Rendered429OpensOneCircuitAndRecordsExplicitFlightEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var breaker = new GlobalChatGptRateLimitCircuitBreaker(() => now, (_, _) => Task.CompletedTask);
        var before = RuntimeFlightRecorder.Snapshot().LastSequence;
        var transitions = 0;
        breaker.StatusChanged += _ => transitions++;

        breaker.ObserveVisibleState("HTTP 429 — Too many requests");
        breaker.ObserveVisibleState("HTTP 429 — Too many requests");

        Assert.True(breaker.IsActive);
        Assert.Equal(1, transitions);
        Assert.Contains(RuntimeFlightRecorder.Snapshot().Events,
            item => item.Sequence > before && item.Category == "Monitor" && item.Action == "RateLimitCircuitOpened");
    }

    private static string RepositoryPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GPTDeskTop.sln")))
            directory = directory.Parent;
        return Path.Combine(new[] { directory?.FullName ?? throw new DirectoryNotFoundException() }.Concat(parts).ToArray());
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from);
        return source[from..to];
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri?.ToString() ?? string.Empty);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        }
    }
}

[CollectionDefinition("Generation recovery safety", DisableParallelization = true)]
public sealed class GenerationRecoverySafetyCollection;

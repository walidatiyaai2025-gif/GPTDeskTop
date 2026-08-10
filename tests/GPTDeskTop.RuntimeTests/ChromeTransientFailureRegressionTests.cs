using System.Reflection;
using System.Text.Json;
using GPTDeskTop.Configuration;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ChromeTransientFailureRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void PromiseCollectedIsRecognizedByRuntimeEvaluateRetryPolicy()
    {
        var method = typeof(ChromeDevToolsService).GetMethod(
            "IsTransientPromiseCollected",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var transient = new InvalidOperationException("Chrome DevTools error: Promise was collected");
        var unrelated = new InvalidOperationException("Chrome DevTools error: Execution context was destroyed");

        Assert.True((bool)method!.Invoke(null, [transient])!);
        Assert.False((bool)method.Invoke(null, [unrelated])!);
    }

    [Fact]
    public void PromiseCollectedRemainsTransientAtMonitorBoundary()
    {
        var method = typeof(ChatGptMonitorService).GetMethod(
            "IsTransientChromeException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var transient = new InvalidOperationException("Runtime.evaluate failed: Promise was collected");
        var unrelated = new InvalidOperationException("Persistent application failure");

        Assert.True((bool)method!.Invoke(null, [transient])!);
        Assert.False((bool)method.Invoke(null, [unrelated])!);
    }

    [Fact]
    public void RuntimeEvaluateRetriesBeforeEscalationWithoutLoggingEveryAttempt()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        var evaluateStart = source.IndexOf(
            "private async Task<JsonElement> EvaluateAsync",
            StringComparison.Ordinal);
        var classifierStart = source.IndexOf(
            "private static bool IsTransientPromiseCollected",
            evaluateStart,
            StringComparison.Ordinal);

        Assert.True(evaluateStart >= 0);
        Assert.True(classifierStart > evaluateStart);

        var evaluateBody = source[evaluateStart..classifierStart];
        Assert.Contains("attempt <= 3", evaluateBody, StringComparison.Ordinal);
        Assert.Contains("IsTransientPromiseCollected(ex) && attempt < 3", evaluateBody, StringComparison.Ordinal);
        Assert.Contains("_retryDelay(TimeSpan.FromMilliseconds(120 * attempt)", evaluateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionLogService.Log", evaluateBody, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorTransientRetryBranchDoesNotWriteCrashDiagnostics()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        var transientCatch = source.IndexOf(
            "catch (Exception ex) when (IsTransientChromeException(ex)) { transientFailures++;",
            StringComparison.Ordinal);
        var genericCatch = source.IndexOf(
            "catch (Exception ex) { Activity?.Invoke(monitor.Id, $\"Monitor exception logged:",
            transientCatch,
            StringComparison.Ordinal);

        Assert.True(transientCatch >= 0);
        Assert.True(genericCatch > transientCatch);

        var transientBranch = source[transientCatch..genericCatch];
        Assert.Contains("Background retry continues", transientBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionLogService.Log", transientBranch, StringComparison.Ordinal);
    }
    [Fact]
    public async Task RuntimeEvaluateRetriesTwoPromiseCollectedFailuresThenSucceedsWithoutDiagnostics()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();
        var diagnostics = new List<string>();
        void CaptureDiagnostic(string message) => diagnostics.Add(message);
        ExceptionLogService.Logged += CaptureDiagnostic;

        try
        {
            var service = new ChromeDevToolsService(
                new HttpClient(),
                new ChromeConfig(),
                (_, method, _, _, extractRuntimeValue) =>
                {
                    Assert.Equal("Runtime.evaluate", method);
                    Assert.True(extractRuntimeValue);
                    attempts++;
                    if (attempts <= 2)
                        throw new InvalidOperationException("Chrome DevTools error: Promise was collected");

                    using var document = JsonDocument.Parse("""{"assistantCount":1,"lastAssistantText":"ready","isGenerating":false,"errorText":""}""");
                    return Task.FromResult(document.RootElement.Clone());
                },
                (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

            var state = await service.GetChatStateAsync(new ChromeTab(), CancellationToken.None);

            Assert.Equal(3, attempts);
            Assert.Equal([TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(240)], delays);
            Assert.Equal("ready", state.LastAssistantText);
            Assert.Empty(diagnostics);
        }
        finally
        {
            ExceptionLogService.Logged -= CaptureDiagnostic;
        }
    }
}

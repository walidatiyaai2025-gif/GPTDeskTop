using System.Reflection;
using System.Text.Json;
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

    [Theory]
    [InlineData("Inspected target navigated or closed", true)]
    [InlineData("No target with given id found", true)]
    [InlineData("Execution context was destroyed.", true)]
    [InlineData("Cannot find context with specified id", true)]
    [InlineData("Cannot find default execution context", true)]
    [InlineData("Invalid parameters", false)]
    public void TargetLifecycleClassifierRecognizesRecoverableCdpFailures(string message, bool expected)
    {
        var poolType = typeof(ChromeDevToolsService).Assembly.GetType(
            "GPTDeskTop.Services.ChromeDevToolsSessionPool");
        var sessionType = poolType?.GetNestedType("DevToolsSession", BindingFlags.NonPublic);
        var method = sessionType?.GetMethod(
            "IsTargetLifecycleError",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            code = -32000,
            message
        }));

        Assert.Equal(expected, (bool)method!.Invoke(null, [document.RootElement.Clone()])!);
    }

    [Fact]
    public void TargetLifecycleCdpFailuresAreTranslatedToTransientIoFailures()
    {
        var sessionPoolSource = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs"));
        var classifierSource = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeTransportFailureClassifier.cs"));

        Assert.Contains("if (IsTargetLifecycleError(error))", sessionPoolSource, StringComparison.Ordinal);
        Assert.Contains("throw new IOException(devToolsError);", sessionPoolSource, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(devToolsError);", sessionPoolSource, StringComparison.Ordinal);
        Assert.Contains(
            "=> ChromeTransportFailureClassifier.IsTargetLifecycleError(error);",
            sessionPoolSource,
            StringComparison.Ordinal);
        Assert.Contains("Inspected target navigated or closed", classifierSource, StringComparison.Ordinal);
        Assert.Contains("Cannot find default execution context", classifierSource, StringComparison.Ordinal);
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
        Assert.Contains("Task.Delay(120 * attempt", evaluateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionLogService.Log", evaluateBody, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorTransientRetryBranchDoesNotWriteCrashDiagnostics()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        var transientCatch = source.IndexOf(
            "catch (Exception ex) when (IsTransientChromeException(ex))",
            StringComparison.Ordinal);
        Assert.True(transientCatch >= 0);

        var transientFailureIncrement = source.IndexOf("transientFailures++;", transientCatch, StringComparison.Ordinal);
        var genericCatch = source.IndexOf("catch (Exception ex)", transientFailureIncrement + "transientFailures++;".Length, StringComparison.Ordinal);

        Assert.True(transientFailureIncrement > transientCatch);
        Assert.True(genericCatch > transientFailureIncrement);

        var transientBranch = source[transientCatch..genericCatch];
        Assert.Contains("Background retry continues", transientBranch, StringComparison.Ordinal);
        Assert.Contains("Task.Delay", transientBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionLogService.Log", transientBranch, StringComparison.Ordinal);
    }
}

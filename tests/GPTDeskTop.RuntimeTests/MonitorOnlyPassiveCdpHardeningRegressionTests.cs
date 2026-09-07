namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorOnlyPassiveCdpHardeningRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void PassiveRuntimeEvaluateHasIndependentThirtySecondTimeout()
    {
        var pool = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs");
        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("PassiveRuntimeEvaluateTimeout = TimeSpan.FromSeconds(30)", pool, StringComparison.Ordinal);
        Assert.Contains("ReadChatStatePassiveAsync", chrome, StringComparison.Ordinal);
        Assert.Contains("ChromeDevToolsSessionPool.PassiveRuntimeEvaluateTimeout", chrome, StringComparison.Ordinal);
    }

    [Fact]
    public void PassiveReaderUsesTypedApiAndClearsCurrentErrorAfterRecovery()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        Assert.DoesNotContain("System.Reflection", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("PassiveStateReader.Invoke", runner, StringComparison.Ordinal);
        Assert.Contains("chrome.ReadChatStatePassiveAsync", runner, StringComparison.Ordinal);
        Assert.Contains("_lastError = string.Empty", runner, StringComparison.Ordinal);
        Assert.Contains("_consecutivePassiveReadFailures = 0", runner, StringComparison.Ordinal);
        Assert.Contains("_lastRecovery = attempt > 1", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorOnlyDisablesBrowserMutatingRecovery()
    {
        var session = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");
        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");

        Assert.Contains("allowBrowserMutationRecovery: false", session, StringComparison.Ordinal);
        Assert.Contains("if (!_allowBrowserMutationRecovery)", chrome, StringComparison.Ordinal);
        Assert.Contains("TryPassiveRebindConversationAsync", chrome, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorSeparatesTotalRetriesConsecutiveFailuresAndRecoveredTransientError()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var form = ReadSource("src", "GPTDeskTop", "UI", "SimpleMonitorForm.cs");
        Assert.Contains("ConsecutivePassiveReadFailures", runner, StringComparison.Ordinal);
        Assert.Contains("LastRecovery", runner, StringComparison.Ordinal);
        Assert.Contains("LastTransientError", runner, StringComparison.Ordinal);
        Assert.Contains("Total CDP retries", form, StringComparison.Ordinal);
        Assert.Contains("Consecutive:", form, StringComparison.Ordinal);
        Assert.Contains("Last transient (recovered)", form, StringComparison.Ordinal);
    }
}

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorRuntimeSafetyRegressionTests
{
    private static string ReadSource()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "MonitorRuntimeSafetyBootstrap.cs"));
        return File.ReadAllText(path);
    }

    [Fact]
    public void RuntimeSafetyContractIsPresent()
    {
        var source = ReadSource();
        Assert.Contains("[ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("EndpointFailureThreshold = 4", source, StringComparison.Ordinal);
        Assert.Contains("TryLaunchDedicatedMonitorChrome", source, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('click', state.clickHandler, true)", source, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('keydown', state.keyHandler, true)", source, StringComparison.Ordinal);
        Assert.Contains("assistantCount() > state.assistantCountAtSend && !isGenerating()", source, StringComparison.Ordinal);
        Assert.Contains("send-storm-suppressed", source, StringComparison.Ordinal);
        Assert.Contains("monitor-runtime-safety.jsonl", source, StringComparison.Ordinal);
    }
}

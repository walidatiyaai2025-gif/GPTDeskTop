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
        Assert.Contains("state.armed && state.accepted && assistantCount() > state.assistantCountAtSend && !isGenerating()", source, StringComparison.Ordinal);
        Assert.Contains("send-storm-suppressed", source, StringComparison.Ordinal);
        Assert.Contains("monitor-runtime-safety.jsonl", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SendStormGuardCannotStayArmedOnStaleStreamingMarkers()
    {
        var source = ReadSource();

        Assert.Contains("const version = 3;", source, StringComparison.Ordinal);
        Assert.Contains("return visible(stop);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[data-is-streaming=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[data-streaming=", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".result-streaming", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UnacceptedPhysicalSendCannotSuppressOutboundForever()
    {
        var source = ReadSource();

        Assert.Contains("const unacceptedArmTimeoutMs = 12000;", source, StringComparison.Ordinal);
        Assert.Contains("const userCount = () => document.querySelectorAll('[data-message-author-role=\"user\"]').length;", source, StringComparison.Ordinal);
        Assert.Contains("userCount() > state.userCountAtSend || assistantCount() > state.assistantCountAtSend", source, StringComparison.Ordinal);
        Assert.Contains("Date.now() - state.armedAt >= unacceptedArmTimeoutMs", source, StringComparison.Ordinal);
        Assert.Contains("existing.refreshLifecycle?.();", source, StringComparison.Ordinal);
        Assert.Contains("state.recoveredCount++;", source, StringComparison.Ordinal);
        Assert.Contains("send-guard-auto-released", source, StringComparison.Ordinal);
        Assert.Contains("UnacceptedTimeout", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedSendRemainsProtectedUntilAssistantTurnCompletes()
    {
        var source = ReadSource();

        Assert.Contains("state.armed && state.accepted && assistantCount() > state.assistantCountAtSend && !isGenerating()", source, StringComparison.Ordinal);
        Assert.Contains("if (unacceptedAttemptExpired())", source, StringComparison.Ordinal);
        Assert.Contains("state.armed && !state.accepted", source, StringComparison.Ordinal);
        Assert.Contains("state.accepted = true;", source, StringComparison.Ordinal);
    }
}

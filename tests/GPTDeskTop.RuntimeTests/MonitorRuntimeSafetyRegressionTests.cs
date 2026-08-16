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
        Assert.Contains("const messageSnapshot = role =>", source, StringComparison.Ordinal);
        Assert.Contains("const snapshotChanged = (current, baseline) =>", source, StringComparison.Ordinal);
        Assert.Contains("send-storm-suppressed", source, StringComparison.Ordinal);
        Assert.Contains("send-guard-accepted-handoff", source, StringComparison.Ordinal);
        Assert.Contains("monitor-runtime-safety.jsonl", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SendStormGuardCannotStayArmedOnStaleStreamingMarkers()
    {
        var source = ReadSource();

        Assert.Contains("const version = 4;", source, StringComparison.Ordinal);
        Assert.Contains("const isGenerating = () => !!findStopButton();", source, StringComparison.Ordinal);
        Assert.Contains("stop generating|stop responding|إيقاف الإنشاء|إيقاف الرد", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[data-is-streaming=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[data-streaming=", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".result-streaming", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UnacceptedPhysicalSendCannotSuppressOutboundForever()
    {
        var source = ReadSource();

        Assert.Contains("const unacceptedArmTimeoutMs = 12000;", source, StringComparison.Ordinal);
        Assert.Contains("if (userTurnAdvanced() || assistantTurnAdvanced())", source, StringComparison.Ordinal);
        Assert.Contains("Date.now() - state.armedAt >= unacceptedArmTimeoutMs", source, StringComparison.Ordinal);
        Assert.Contains("existing.refreshLifecycle?.();", source, StringComparison.Ordinal);
        Assert.Contains("state.recoveredCount++;", source, StringComparison.Ordinal);
        Assert.Contains("send-guard-auto-released", source, StringComparison.Ordinal);
        Assert.Contains("UnacceptedTimeout", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedCommittedTurnCannotSuppressOutboundForever()
    {
        var source = ReadSource();

        Assert.Contains("const acceptedHandoffTimeoutMs = 8000;", source, StringComparison.Ordinal);
        Assert.Contains("state.acceptedAt = Date.now();", source, StringComparison.Ordinal);
        Assert.Contains("Date.now() - state.acceptedAt >= acceptedHandoffTimeoutMs", source, StringComparison.Ordinal);
        Assert.Contains("state.acceptedHandoffCount++;", source, StringComparison.Ordinal);
        Assert.Contains("send-guard-accepted-handoff", source, StringComparison.Ordinal);
        Assert.Contains("AcceptedTurnTimeout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("assistantCount() > state.assistantCountAtSend", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DomVirtualizationCannotHideAcceptedOrCompletedTurnProgress()
    {
        var source = ReadSource();

        Assert.Contains("current.node !== baseline.node", source, StringComparison.Ordinal);
        Assert.Contains("current.id !== baseline.id", source, StringComparison.Ordinal);
        Assert.Contains("current.textLength !== baseline.textLength", source, StringComparison.Ordinal);
        Assert.Contains("current.textTail !== baseline.textTail", source, StringComparison.Ordinal);
        Assert.Contains("state.userSnapshotAtSend = messageSnapshot('user');", source, StringComparison.Ordinal);
        Assert.Contains("state.assistantSnapshotAtSend = messageSnapshot('assistant');", source, StringComparison.Ordinal);
        Assert.Contains("assistantTurnAdvanced() && !isGenerating()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerationSignalStillBlocksPhysicalSendAfterGuardHandoff()
    {
        var source = ReadSource();

        Assert.Contains("if (isGenerating()) return block(event);", source, StringComparison.Ordinal);
        Assert.Contains("if (state.armed) return block(event);", source, StringComparison.Ordinal);
        Assert.Contains("if (isGenerating() || (assistantTurnAdvanced() && !isGenerating()))", source, StringComparison.Ordinal);
        Assert.Contains("disarm();", source, StringComparison.Ordinal);
    }
}

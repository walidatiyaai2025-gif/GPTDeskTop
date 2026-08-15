namespace GPTDeskTop.RuntimeTests;

public sealed class RuntimeFlightRecorderIntegrationRegressionTests
{
    [Fact]
    public void InspectorProjectsAndExportsFlightRecorderTimeline()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");
        Assert.Contains("RuntimeFlightSnapshot FlightRecorder", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeFlightRecorder.Snapshot()", source, StringComparison.Ordinal);
        Assert.Contains("Flight recorder:", source, StringComparison.Ordinal);
        Assert.Contains("runtime-flight-recorder.json", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CdpInstrumentationRecordsMethodAndHashedTargetContextWithoutRawPayload()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs");
        Assert.Contains("\"CDP\", \"CommandRequested\"", source, StringComparison.Ordinal);
        Assert.Contains("\"CDP\", \"CommandCompleted\"", source, StringComparison.Ordinal);
        Assert.Contains("tabId: tab.Id, conversationRef: tab.Url", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeFlightRecorder.Record(\"CDP\", \"CommandRequested\", \"started\", parameters", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeFlightRecorder.Record(\"CDP\", \"CommandCompleted\", \"success\", parameters", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorTraceRecordsOnlyChangedMonitorAndBrowserTargetState()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "MonitorDiagnosticTraceService.cs");
        Assert.Contains("RuntimeFlightRecorder.Record(", source, StringComparison.Ordinal);
        Assert.Contains("\"Browser\"", source, StringComparison.Ordinal);
        Assert.Contains("\"TargetChanged\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Monitor\"", source, StringComparison.Ordinal);
        Assert.Contains("\"StateChanged\"", source, StringComparison.Ordinal);
        Assert.Contains("_lastTargetIds", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OutboundCoordinatorCreatesPerMonitorScopeBeforePhysicalSubmit()
    {
        var source = ReadSource("src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs");
        var scopeIndex = source.IndexOf("RuntimeFlightRecorder.BeginScope(monitorId, conversationKey)", StringComparison.Ordinal);
        var sendIndex = source.IndexOf("accepted = await physicalSend()", StringComparison.Ordinal);
        Assert.True(scopeIndex >= 0);
        Assert.True(sendIndex > scopeIndex);
        Assert.DoesNotContain("RuntimeFlightRecorder.Record(\"Delivery\", message", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReloadPathFlowsThroughInstrumentedCdpPool()
    {
        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("ReloadTabAsync", chrome, StringComparison.Ordinal);
        Assert.Contains("\"Page.reload\"", chrome, StringComparison.Ordinal);
        Assert.Contains("=> _sessionPool.SendCommandAsync", chrome, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}

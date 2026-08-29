namespace GPTDeskTop.RuntimeTests;

public sealed class FieldDeliveryTimeoutRecoveryRegressionTests
{
    [Fact]
    public void RoutineRuntimeEvaluateSuccessesAreSuppressedButFailuresRemainRecorded()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs");
        Assert.Contains("ShouldRecordCommandLifecycle", source, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(method, \"Runtime.evaluate\", StringComparison.Ordinal)", source, StringComparison.Ordinal);
        Assert.Contains("if (recordCommandLifecycle)", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeFlightRecorder.Record(\"CDP\", \"CommandCompleted\", \"failed\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorPollCarriesMonitorAndConversationCorrelationIntoCdpCalls()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        Assert.Contains("RuntimeFlightRecorder.BeginScope(monitor.Id)", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeFlightRecorder.BeginScope(monitor.Id, tab.Id, tab.Url)", source, StringComparison.Ordinal);
        var scope = source.IndexOf("using var pollFlightScope = RuntimeFlightRecorder.BeginScope(monitor.Id, tab.Id, tab.Url)", StringComparison.Ordinal);
        var read = source.IndexOf("var state = await _chrome.GetChatStateAsync(tab, cancellationToken)", scope, StringComparison.Ordinal);
        Assert.True(scope >= 0 && read > scope);
    }

    [Fact]
    public void StructuredRenderedErrorBypassesPassiveUnchangedResponseWait()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var error = source.IndexOf("var isError = !state.IsGenerating && !string.IsNullOrWhiteSpace(state.ErrorText)", StringComparison.Ordinal);
        var passive = source.IndexOf("if (!isError && (state.IsGenerating || string.IsNullOrWhiteSpace(text)", StringComparison.Ordinal);
        Assert.True(error >= 0 && passive > error);
        Assert.Contains("RenderedErrorObserved", source, StringComparison.Ordinal);
        Assert.Contains("message-delivery-timeout", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliveryTimeoutUsesFreshChatContinuationNotBlindOriginalResend()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        Assert.Contains("if (isError && IsDeliveryTimeout(text))", source, StringComparison.Ordinal);
        Assert.Contains("DeliveryTimeoutRecovery", source, StringComparison.Ordinal);
        Assert.Contains("SendWhenReadyAsync(monitor.Id, newTab, recoveryMessage", source, StringComparison.Ordinal);
        Assert.Contains("rotationTrigger: \"DeliveryTimeout\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendWhenReadyAsync(monitor.Id, newTab, text", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeFlightRecorder.Record(\"Monitor\", \"RenderedErrorObserved\", \"error\", text", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RetryCardDetectorIsTargetedAndDoesNotScanConversationBodyGlobally()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("version === 8", source, StringComparison.Ordinal);
        Assert.Contains("const version = 8", source, StringComparison.Ordinal);
        Assert.Contains("const isCurrentTurnElement = element =>", source, StringComparison.Ordinal);
        Assert.Contains("button,[role=\"button\"]", source, StringComparison.Ordinal);
        Assert.Contains("retry", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("depth < 5", source, StringComparison.Ordinal);
        Assert.Contains("text.length > 600", source, StringComparison.Ordinal);
        Assert.DoesNotContain("document.body?.innerText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("document.body.innerText", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorMarksOldGlobalComposerAndSendDiagnosticsAsStale()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");
        Assert.Contains("DiagnosticStaleAfter = TimeSpan.FromMinutes(5)", source, StringComparison.Ordinal);
        Assert.Contains("double AgeSeconds", source, StringComparison.Ordinal);
        Assert.Contains("bool IsStale", source, StringComparison.Ordinal);
        Assert.Contains("| age: {composer.AgeSeconds:0}s | stale:", source, StringComparison.Ordinal);
        Assert.Contains("| age: {verifiedSend.AgeSeconds:0}s | stale:", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}


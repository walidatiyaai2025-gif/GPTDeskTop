namespace GPTDeskTop.RuntimeTests;

public sealed class FieldUncertainDeliveryResponseReconciliationTests
{
    [Fact]
    public void StableNonErrorAssistantResponseCompletesPriorDeliveryBeforeNextContinuation()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var stableBoundary = source.IndexOf(
            "if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds) continue;",
            StringComparison.Ordinal);
        var handled = source.IndexOf("lastHandledText = text;", stableBoundary, StringComparison.Ordinal);
        var nonErrorGate = source.IndexOf("if (!isError)", handled, StringComparison.Ordinal);
        var complete = source.IndexOf("_outboundDelivery.MarkCompleted(monitor.Id);", nonErrorGate, StringComparison.Ordinal);
        var inboundLog = source.IndexOf("await _database.AddLogAsync(\"Inbound\"", complete, StringComparison.Ordinal);
        var nextAutoSend = source.IndexOf("var autoSent = await SendWhenReadyAsync(", inboundLog, StringComparison.Ordinal);

        Assert.True(stableBoundary >= 0);
        Assert.True(handled > stableBoundary);
        Assert.True(nonErrorGate > handled);
        Assert.True(complete > nonErrorGate);
        Assert.True(inboundLog > complete);
        Assert.True(nextAutoSend > inboundLog);
    }

    [Fact]
    public void ErrorResponseDoesNotReleaseUncertainDeliveryGate()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var stableBoundary = source.IndexOf("lastHandledText = text;", StringComparison.Ordinal);
        var complete = source.IndexOf("_outboundDelivery.MarkCompleted(monitor.Id);", stableBoundary, StringComparison.Ordinal);
        var localWindow = source.Substring(stableBoundary, complete - stableBoundary + "_outboundDelivery.MarkCompleted(monitor.Id);".Length);

        Assert.Contains("if (!isError)", localWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("if (isError)\n                        _outboundDelivery.MarkCompleted", localWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionOnlyTransitionsSettledAcceptedOrUncertainOperations()
    {
        var source = ReadSource("src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs");

        Assert.Contains(
            "state.Phase is not (OutboundDeliveryPhase.Accepted or OutboundDeliveryPhase.ReconcileRequired)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("response-observed-after-uncertain-send", source, StringComparison.Ordinal);
        Assert.Contains("Phase = OutboundDeliveryPhase.Completed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConcurrentSendingAndUnreconciledDeliveryRemainDuplicateSuppressed()
    {
        var source = ReadSource("src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs");
        var duplicateGuard = source.IndexOf("IsDuplicateInFlight(previous, conversationKey, fingerprint)", StringComparison.Ordinal);
        var suppressed = source.IndexOf("DuplicateSuppressed", duplicateGuard, StringComparison.Ordinal);
        var physicalSubmit = source.IndexOf("PhysicalSubmitRequested", suppressed, StringComparison.Ordinal);

        Assert.True(duplicateGuard >= 0 && suppressed > duplicateGuard && physicalSubmit > suppressed);
        Assert.Contains(
            "previous.Phase is OutboundDeliveryPhase.Sending or OutboundDeliveryPhase.ReconcileRequired",
            source,
            StringComparison.Ordinal);
        Assert.Contains("receipt-not-confirmed; no blind retry", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorAdvancesHandledResponseBeforeAnyAutoSendAttempt()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var handled = source.IndexOf("lastHandledText = text;", StringComparison.Ordinal);
        var complete = source.IndexOf("_outboundDelivery.MarkCompleted(monitor.Id);", handled, StringComparison.Ordinal);
        var autoSend = source.IndexOf("var autoSent = await SendWhenReadyAsync(", complete, StringComparison.Ordinal);

        Assert.True(handled >= 0 && complete > handled && autoSend > complete);
    }

    [Fact]
    public void VerifiedSendStillWaitsDuringPostSubmitGenerationInsteadOfBlindRetrying()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");

        Assert.Contains("generation-after-submit", source, StringComparison.Ordinal);
        Assert.Contains("physical-submit-unacknowledged", source, StringComparison.Ordinal);
        Assert.Contains("verified-send-deadline-without-receipt", source, StringComparison.Ordinal);
        Assert.Contains("receipt-missing-after-refresh", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}

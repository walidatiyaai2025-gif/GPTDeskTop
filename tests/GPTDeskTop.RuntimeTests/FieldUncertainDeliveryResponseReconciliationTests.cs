namespace GPTDeskTop.RuntimeTests;

public sealed class FieldUncertainDeliveryResponseReconciliationTests
{
    [Fact]
    public void StableNonErrorAssistantResponseCompletesPriorDeliveryBeforeNextContinuation()
    {
        var source = NormalizeWhitespace(ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        const string stableSequence =
            "if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds) continue; " +
            "lastHandledText = text; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; " +
            "if (!isError) _outboundDelivery.MarkCompleted(monitor.Id); " +
            "await _database.AddLogAsync(\"Inbound\"";

        Assert.Contains(stableSequence, source, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorResponseDoesNotReleaseUncertainDeliveryGate()
    {
        var source = NormalizeWhitespace(ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));

        Assert.Contains(
            "if (!isError) _outboundDelivery.MarkCompleted(monitor.Id);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "if (isError) _outboundDelivery.MarkCompleted(monitor.Id);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionOnlyTransitionsSettledAcceptedOrUncertainOperations()
    {
        var source = NormalizeWhitespace(ReadSource("src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs"));

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
        var source = NormalizeWhitespace(ReadSource("src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs"));

        Assert.Contains(
            "previous.Phase is OutboundDeliveryPhase.Sending or OutboundDeliveryPhase.ReconcileRequired",
            source,
            StringComparison.Ordinal);
        Assert.Contains("DuplicateSuppressed", source, StringComparison.Ordinal);
        Assert.Contains("uncertain-or-in-flight", source, StringComparison.Ordinal);
        Assert.Contains("receipt-not-confirmed; no blind retry", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorAdvancesHandledResponseBeforeAnyAutoSendAttempt()
    {
        var source = NormalizeWhitespace(ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
        var handled = source.IndexOf(
            "lastHandledText = text; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; if (!isError) _outboundDelivery.MarkCompleted(monitor.Id);",
            StringComparison.Ordinal);
        var autoSend = source.IndexOf("var autoSent = await SendWhenReadyAsync(", StringComparison.Ordinal);

        Assert.True(handled >= 0, "Stable response completion boundary was not found.");
        Assert.True(autoSend > handled, "Auto reply must occur after stable response reconciliation.");
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

    private static string NormalizeWhitespace(string source)
        => string.Join(
            " ",
            source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}

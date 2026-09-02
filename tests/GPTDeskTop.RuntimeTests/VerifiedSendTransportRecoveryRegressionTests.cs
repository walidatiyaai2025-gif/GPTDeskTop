namespace GPTDeskTop.RuntimeTests;

public sealed class VerifiedSendTransportRecoveryRegressionTests
{
    private static string ServiceSource()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));
        return File.ReadAllText(path);
    }

    private static string VerifiedSendMethod(string source)
    {
        var methodStart = source.IndexOf("public async Task<bool> SendChatMessageVerifiedAsync", StringComparison.Ordinal);
        var helperStart = source.IndexOf("private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && helperStart > methodStart);
        return source[methodStart..helperStart];
    }

    [Fact]
    public void InitialVerifiedSendSnapshotUsesBoundedTransientRecoveryInsteadOfEscapingTimeout()
    {
        var method = VerifiedSendMethod(ServiceSource());

        Assert.Contains("var deadline = DateTimeOffset.UtcNow.AddSeconds(30);", method, StringComparison.Ordinal);
        Assert.Contains("var before = await WaitForStableUserMessageBaselineAsync(tab, deadline, cancellationToken);", method, StringComparison.Ordinal);
        Assert.Contains("stableReadsRequired = 5", method, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(2)", method, StringComparison.Ordinal);
        Assert.Contains("IsRecoverableMonitorTransportException(ex)", method, StringComparison.Ordinal);
        Assert.Contains("TryRefreshTabBindingAsync(tab, cancellationToken)", method, StringComparison.Ordinal);
        Assert.Contains("baseline-unreadable", method, StringComparison.Ordinal);
        Assert.DoesNotContain("var before = await GetUserMessageSnapshotAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoverableRuntimeEvaluateTimeoutRetiresSessionWithoutWritingCrashNoise()
    {
        var source = ServiceSource();
        var helperStart = source.IndexOf("private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync", StringComparison.Ordinal);
        var rawSnapshotStart = source.IndexOf("private async Task<(int Count, string LastText)> GetUserMessageSnapshotAsync", helperStart, StringComparison.Ordinal);

        Assert.True(helperStart >= 0);
        Assert.True(rawSnapshotStart > helperStart);

        var helper = source[helperStart..rawSnapshotStart];
        Assert.Contains("IsRecoverableMonitorTransportException(ex)", helper, StringComparison.Ordinal);
        Assert.Contains("_sessionPool.Invalidate(tab.Id);", helper, StringComparison.Ordinal);
        Assert.Contains("return (false, 0, string.Empty);", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("ExceptionLogService.Log", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulJavascriptClickRequiresObservableAcceptanceBeforeConsumingSubmitAuthority()
    {
        var method = VerifiedSendMethod(ServiceSource());

        Assert.Contains("const int maxSubmitAttempts = 2;", method, StringComparison.Ordinal);
        Assert.Contains("const int maxUnacceptedClickAttempts = 3;", method, StringComparison.Ordinal);
        Assert.Contains("var receiptGrace = TimeSpan.FromSeconds(3);", method, StringComparison.Ordinal);
        Assert.Contains("ObserveImmediatePhysicalSubmitAsync", method, StringComparison.Ordinal);
        Assert.Contains("ImmediatePhysicalSubmitObservation.ReceiptConfirmed", method, StringComparison.Ordinal);
        Assert.Contains("ImmediatePhysicalSubmitObservation.AcceptedTransition", method, StringComparison.Ordinal);
        Assert.Contains("ImmediatePhysicalSubmitObservation.ClickNotAccepted", method, StringComparison.Ordinal);
        Assert.Contains("click-not-accepted-composer-still-ready", method, StringComparison.Ordinal);
        Assert.Contains("physical-submit-ambiguous-after-click", method, StringComparison.Ordinal);
        Assert.Contains("unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow", method, StringComparison.Ordinal);
        Assert.Contains("current.Count > before.Count && string.Equals(current.LastText, expected", method, StringComparison.Ordinal);
        Assert.Contains("ReconcileUnacknowledgedSubmitAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("physical-submit-unacknowledged", method, StringComparison.Ordinal);
        Assert.DoesNotContain("physicalSendAccepted", method, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoverableTransportDuringSubmitReconcilesBeforeAnyRetry()
    {
        var method = VerifiedSendMethod(ServiceSource());
        var send = method.IndexOf("submitted = await SendChatMessageAsync(tab, message", StringComparison.Ordinal);
        var transientCatch = method.IndexOf("IsRecoverableMonitorTransportException(ex)", send, StringComparison.Ordinal);
        var uncertain = method.IndexOf("transport-uncertain-submit", transientCatch, StringComparison.Ordinal);
        var continueAfterUncertain = method.IndexOf("continue;", uncertain, StringComparison.Ordinal);

        Assert.True(send >= 0 && transientCatch > send && uncertain > transientCatch && continueAfterUncertain > uncertain);
        Assert.Contains("submitAttempts++;", method[transientCatch..continueAfterUncertain], StringComparison.Ordinal);
        Assert.Contains("unacknowledgedSubmitSinceUtc = DateTimeOffset.UtcNow;", method[transientCatch..continueAfterUncertain], StringComparison.Ordinal);
        Assert.DoesNotContain("SendChatMessageAsync(tab, message", method[transientCatch..continueAfterUncertain], StringComparison.Ordinal);
    }

    [Fact]
    public void ReconciliationNeedsHydratedStablePostRebindEvidenceBeforeAuthorizingOneRetry()
    {
        var source = ServiceSource();
        var helperStart = source.IndexOf("private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync", StringComparison.Ordinal);
        var snapshotStart = source.IndexOf("private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync", helperStart, StringComparison.Ordinal);
        Assert.True(helperStart >= 0 && snapshotStart > helperStart);
        var helper = source[helperStart..snapshotStart];

        Assert.Contains("TryRefreshTabBindingAsync(tab, cancellationToken)", helper, StringComparison.Ordinal);
        Assert.Contains("PostSubmitReloadSuppressed", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshStuckComposerAsync", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("Page.reload", helper, StringComparison.Ordinal);
        Assert.Contains("ChatGptConversationIdentity.IsSame(originalUrl, tab.Url)", helper, StringComparison.Ordinal);
        Assert.Contains("MonitorDeliveryRecoveryPolicy.ClassifyPostRefreshUserTurn", helper, StringComparison.Ordinal);
        Assert.Contains("PostRefreshUserTurnObservation.Hydrating", helper, StringComparison.Ordinal);
        Assert.Contains("var stableAbsenceReads = 0;", helper, StringComparison.Ordinal);
        Assert.Contains("var stableUnexpectedReads = 0;", helper, StringComparison.Ordinal);
        Assert.Contains("stableAbsenceReads++;", helper, StringComparison.Ordinal);
        Assert.Contains("stableAbsenceReads >= 4", helper, StringComparison.Ordinal);
        Assert.Contains("stableUnexpectedReads >= 2", helper, StringComparison.Ordinal);
        Assert.Contains("UnacknowledgedSubmitReconciliationResult.RetryAuthorized", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratingOrRenderedErrorFailsClosedInsteadOfBlindlyRetrying()
    {
        var method = VerifiedSendMethod(ServiceSource());

        Assert.Contains("pendingReadiness.HasRenderedError", method, StringComparison.Ordinal);
        Assert.Contains("rendered-error-after-submit", method, StringComparison.Ordinal);
        Assert.Contains("pendingReadiness.IsGenerating", method, StringComparison.Ordinal);
        Assert.Contains("generation-after-submit", method, StringComparison.Ordinal);
        Assert.Contains("ambiguous-post-submit-reconciliation", method, StringComparison.Ordinal);
        Assert.Contains("retry-limit-reached-without-receipt", method, StringComparison.Ordinal);
    }
}

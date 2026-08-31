namespace GPTDeskTop.RuntimeTests;

public sealed class FieldSavedMonitorReconciliationRegressionTests
{
    [Fact]
    public void VerifiedSendWaitsForStableHydratedBaselineBeforeAnyPhysicalSubmit()
    {
        var chrome = ChromeSource();
        var method = Slice(chrome, "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync", "private enum UnacknowledgedSubmitReconciliationResult");
        var send = Slice(chrome, "public async Task<bool> SendChatMessageVerifiedAsync", "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync");

        Assert.Contains("stableReadsRequired = 5", method, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(2)", method, StringComparison.Ordinal);
        Assert.Contains("readiness.EditorPresent", method, StringComparison.Ordinal);
        Assert.Contains("readiness.EditorEnabled", method, StringComparison.Ordinal);
        Assert.Contains("stable-editor-and-user-turn-baseline", method, StringComparison.Ordinal);
        Assert.Contains("WaitForStableUserMessageBaselineAsync(tab, deadline, cancellationToken)", send, StringComparison.Ordinal);
        Assert.Contains("pre-submit-hydration-observed", send, StringComparison.Ordinal);
        Assert.Contains("preSubmitConflictStableReads < 3", send, StringComparison.Ordinal);
    }

    [Fact]
    public void PostSubmitReconciliationIsReadOnlyAndCannotReloadLoop()
    {
        var method = Slice(
            ChromeSource(),
            "private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync",
            "private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync");

        Assert.Contains("TryRefreshTabBindingAsync(tab, cancellationToken)", method, StringComparison.Ordinal);
        Assert.Contains("PostSubmitReloadSuppressed", method, StringComparison.Ordinal);
        Assert.Contains("stableAbsenceReads >= 4", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshStuckComposerAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadTabAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Page.reload", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SendRecoveryReconciliationUsesGlobalOperationGateEvenOutsidePollCycle()
    {
        var monitor = MonitorSource();
        var method = Slice(monitor, "private async Task<bool> SendWhenReadyAsync", "private async Task ApplyModelRouteAsync");

        Assert.Contains("_chatOperationGate.ActiveMonitorId != monitorId", method, StringComparison.Ordinal);
        Assert.Contains("send-recovery-reconciliation", method, StringComparison.Ordinal);
        Assert.Contains("operationLease?.Dispose()", method, StringComparison.Ordinal);
        Assert.Contains("allowRecoveryReload: allowRecoveryReload", method, StringComparison.Ordinal);
    }

    private static string ChromeSource() => ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
    private static string MonitorSource() => ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");

    private static string ReadSource(params string[] parts)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts))));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}

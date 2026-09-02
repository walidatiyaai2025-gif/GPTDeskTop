namespace GPTDeskTop.RuntimeTests;

public sealed class FreshChatPhysicalSubmitFallbackRegressionTests
{
    [Fact]
    public void RejectedVerifiedDeliveryIsReclassifiedWhenExactComposerProvesNoSubmit()
    {
        var source = MonitorSource();
        var method = Slice(source, "private async Task<SendWhenReadyOutcome> SendWhenReadyAsync", "private async Task ApplyModelRouteAsync");
        var accepted = method.IndexOf("if (accepted)", StringComparison.Ordinal);
        var unsentProbe = method.IndexOf("IsComposerDefinitelyStillAwaitingSubmitAsync", StringComparison.Ordinal);
        var reconcile = method.IndexOf("Composer delivery was not confirmed", StringComparison.Ordinal);
        Assert.True(accepted >= 0 && unsentProbe > accepted && reconcile > unsentProbe);
        Assert.Contains("return SendWhenReadyOutcome.DeferredBeforePhysicalSubmit;", method[unsentProbe..reconcile], StringComparison.Ordinal);
    }

    [Fact]
    public void DefinitelyUnsentProbeRequiresStableExactEnabledComposer()
    {
        var source = ChromeSource();
        var method = Slice(source, "public async Task<bool> IsComposerDefinitelyStillAwaitingSubmitAsync", "private async Task<bool> TryNativeFallbackAfterRejectedDomClickAsync");
        Assert.Contains("confirmationsRequired = 3", method, StringComparison.Ordinal);
        Assert.Contains("ComposerEditorMatchesExpectedAsync", method, StringComparison.Ordinal);
        Assert.Contains("!readiness.IsGenerating", method, StringComparison.Ordinal);
        Assert.Contains("!readiness.HasRenderedError", method, StringComparison.Ordinal);
        Assert.Contains("readiness.SendButtonPresent", method, StringComparison.Ordinal);
        Assert.Contains("readiness.SendButtonEnabled", method, StringComparison.Ordinal);
        Assert.Contains("confirmed-unsent-composer", method, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedMouseSubmitFallsBackToTrustedNativeEnterAndReobservesReceipt()
    {
        var source = ChromeSource();
        var method = Slice(source, "public async Task<bool> SendChatMessageVerifiedAsync", "private async Task<(bool Success, int Count, string LastText)> WaitForStableUserMessageBaselineAsync");
        var rejected = method.IndexOf("ImmediatePhysicalSubmitObservation.ClickNotAccepted", StringComparison.Ordinal);
        var enter = method.IndexOf("TryDispatchNativeEnterSubmitAsync", rejected, StringComparison.Ordinal);
        var observe = method.IndexOf("ObserveImmediatePhysicalSubmitAsync", enter, StringComparison.Ordinal);
        Assert.True(rejected >= 0 && enter > rejected && observe > enter);
        Assert.Contains("native-enter-submit-confirmed", method, StringComparison.Ordinal);
        Assert.Contains("native-enter-submit-ambiguous", method, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeEnterUsesRawKeyDownAndKeyUpWithoutCharInsertion()
    {
        var source = ChromeSource();
        var method = Slice(source, "private async Task<bool> TryDispatchNativeEnterSubmitAsync", "public async Task<bool> IsComposerDefinitelyStillAwaitingSubmitAsync");
        Assert.Contains("Input.dispatchKeyEvent", method, StringComparison.Ordinal);
        Assert.Contains("type = \"rawKeyDown\"", method, StringComparison.Ordinal);
        Assert.Contains("type = \"keyUp\"", method, StringComparison.Ordinal);
        Assert.DoesNotContain("type = \"char\"", method, StringComparison.Ordinal);
        Assert.Contains("ComposerEditorMatchesExpectedAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SendButtonSelectionNeverStopsAtHiddenStaleTestIdButton()
    {
        var chrome = ChromeSource();
        var readiness = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChatComposerReadinessScript.cs")));
        Assert.DoesNotContain("document.querySelector('button[data-testid=\"send-button\"]') ||", chrome, StringComparison.Ordinal);
        Assert.DoesNotContain("document.querySelector('button[data-testid=\"send-button\"]') ||", readiness, StringComparison.Ordinal);
        Assert.Contains("button.getAttribute('data-testid') === 'send-button'", chrome, StringComparison.Ordinal);
        Assert.Contains("button.getAttribute('data-testid') === 'send-button'", readiness, StringComparison.Ordinal);
    }

    private static string ChromeSource() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs")));

    private static string MonitorSource() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}

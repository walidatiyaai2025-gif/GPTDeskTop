namespace GPTDeskTop.RuntimeTests;

public sealed class FieldExactlyOnceRuntimeInspectorRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void ProductionSendPathPerformsAtMostOnePhysicalComposerSendPerLogicalAttempt()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var start = source.IndexOf("private async Task<bool> SendWhenReadyAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task ApplyModelRouteAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("_outboundDelivery.SendOnceAsync", method, StringComparison.Ordinal);
        Assert.Contains("_chrome.SendChatMessageVerifiedAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("while (DateTimeOffset.UtcNow < deadline)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("attempt++", method, StringComparison.Ordinal);
        Assert.Contains("suppressed blind resend", method, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeInspectorIsSanitizedAndExportsBoundedSupportBundle()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");
        Assert.Contains("runtime-inspector.json", source, StringComparison.Ordinal);
        Assert.Contains("summary.txt", source, StringComparison.Ordinal);
        Assert.Contains("TakeLast(1000)", source, StringComparison.Ordinal);
        Assert.Contains("github_pat_", source, StringComparison.Ordinal);
        Assert.Contains("Authorization:", source, StringComparison.Ordinal);
        Assert.Contains("cookie", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetSettingAsync(\"GitHub", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorCapturesActualExeMonitorsBrowserUiTreeAndToolStripNavigation()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");
        var monitorDiagnostics = ReadSource("src", "GPTDeskTop", "Services", "MonitorRuntimeDiagnosticReader.cs");

        Assert.Contains("Environment.ProcessPath", source, StringComparison.Ordinal);
        Assert.Contains("MonitorRuntimeDiagnosticReader.Capture(monitor)", source, StringComparison.Ordinal);
        Assert.Contains("_running", monitorDiagnostics, StringComparison.Ordinal);
        Assert.Contains("_sync", monitorDiagnostics, StringComparison.Ordinal);
        Assert.Contains("lock (syncRoot)", monitorDiagnostics, StringComparison.Ordinal);
        Assert.Contains("chrome", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("control.Visible", source, StringComparison.Ordinal);
        Assert.Contains("control.Enabled", source, StringComparison.Ordinal);
        Assert.Contains("control.DeviceDpi", source, StringComparison.Ordinal);
        Assert.Contains("control.Bounds", source, StringComparison.Ordinal);
        Assert.Contains("var forms = ResolveForms(owner);", source, StringComparison.Ordinal);
        Assert.Contains("WalkToolStrips(form, formScope, ui)", source, StringComparison.Ordinal);
        Assert.Contains("DescendantsAndSelf(root).OfType<ToolStrip>()", source, StringComparison.Ordinal);
        Assert.Contains("Kind = \"ToolStripItem\"", source, StringComparison.Ordinal);
        Assert.Contains("FormScope = formScope", source, StringComparison.Ordinal);
        Assert.Contains("item.Text", source, StringComparison.Ordinal);
        Assert.Contains("item.Visible", source, StringComparison.Ordinal);
        Assert.Contains("item.Available", source, StringComparison.Ordinal);
        Assert.Contains("ToolStripDropDownItem", source, StringComparison.Ordinal);
        Assert.Contains("dropDown.DropDownItems", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorCapturesOwnedAuxiliaryFormsAndAttributesRowsToAFormScope()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");

        Assert.Contains("foreach (var owned in owner.OwnedForms)", source, StringComparison.Ordinal);
        Assert.Contains("Application.OpenForms.Cast<Form>().ToArray()", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(form.Owner, owner)", source, StringComparison.Ordinal);
        Assert.Contains("AuxiliaryForm#{index}", source, StringComparison.Ordinal);
        Assert.Contains("FormsCaptured: forms.Count", source, StringComparison.Ordinal);
        Assert.Contains("UI forms:", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorReportsOnlyMaterialVisibleChildOverflowAndIgnoresNormalTopLevelChrome()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");

        Assert.Contains("private const int OverflowToleranceLogicalPixels = 2;", source, StringComparison.Ordinal);
        Assert.Contains("CaptureVisibleOverflow(control, child, formScope, overflows)", source, StringComparison.Ordinal);
        Assert.Contains("var client = parent.ClientSize;", source, StringComparison.Ordinal);
        Assert.Contains("bounds.Right - client.Width", source, StringComparison.Ordinal);
        Assert.Contains("bounds.Bottom - client.Height", source, StringComparison.Ordinal);
        Assert.Contains("parent is ScrollableControl scrollable && scrollable.AutoScroll", source, StringComparison.Ordinal);
        Assert.Contains("VisibleOverflowCount: overflows.Count", source, StringComparison.Ordinal);
        Assert.Contains("visible overflows:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("form.Bounds.X < 0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("owner.Bounds.X < 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerDiagnosticsExposeOnlyDecisionReasonAndTimestamp()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");

        Assert.Contains("ChatComposerDecisionDiagnostics.Last", source, StringComparison.Ordinal);
        Assert.Contains("internal sealed record RuntimeInspectorComposerDiagnostics", source, StringComparison.Ordinal);
        Assert.Contains("string Decision", source, StringComparison.Ordinal);
        Assert.Contains("string Reason", source, StringComparison.Ordinal);
        Assert.Contains("DateTimeOffset ObservedAtUtc", source, StringComparison.Ordinal);
        Assert.Contains("Composer gate:", source, StringComparison.Ordinal);

        var recordStart = source.IndexOf("internal sealed record RuntimeInspectorComposerDiagnostics", StringComparison.Ordinal);
        var recordEnd = source.IndexOf("internal sealed record RuntimeInspectorUiOverflow", recordStart, StringComparison.Ordinal);
        Assert.True(recordStart >= 0 && recordEnd > recordStart);
        var record = source[recordStart..recordEnd];
        Assert.DoesNotContain("Prompt", record, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Message", record, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Text", record, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowserInventoryIsExplicitlySystemWideAndNeverPresentedAsAppOwnership()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");

        Assert.Contains("internal sealed record BrowserProcessDiagnostics", source, StringComparison.Ordinal);
        Assert.Contains("Scope: \"System-wide\"", source, StringComparison.Ordinal);
        Assert.Contains("not asserted to be owned by GPTDeskTop", source, StringComparison.Ordinal);
        Assert.Contains("System browser processes:", source, StringComparison.Ordinal);
        Assert.Contains("Browser scope:", source, StringComparison.Ordinal);
        Assert.Contains("Chrome: browserRows.Count", source, StringComparison.Ordinal);
        Assert.Contains("EdgeOrWebView: browserRows.Count", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nBrowser processes: {snapshot.Browsers.Count}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShutdownProgressUsesResponsivePaddingAndCannotKeepNegativeInnerWidth()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "ShutdownLoadingOverlay.cs");

        Assert.DoesNotContain("new Padding(120, 11, 120, 11)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveProgressPadding(progressHost)", source, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(width / 8, 16, 120)", source, StringComparison.Ordinal);
        Assert.Contains("horizontalPadding * 2 >= width", source, StringComparison.Ordinal);
        Assert.Contains("Math.Max(0, (width - 1) / 2)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsAndRuntimeInspectorAreFirstClassResponsiveNavigation()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "ProjectMonitorUiBootstrap.cs");
        var inspector = ReadSource("src", "GPTDeskTop", "UI", "RuntimeInspectorForm.cs");
        Assert.Contains("Text = \"Projects\"", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Runtime Inspector\"", source, StringComparison.Ordinal);
        Assert.Contains("RetireDuplicateMonitorNavigation", source, StringComparison.Ordinal);
        Assert.Contains("AutoSize = true", source, StringComparison.Ordinal);
        Assert.Contains("WrapContents = true", inspector, StringComparison.Ordinal);
        Assert.Contains("AutoScaleMode = AutoScaleMode.Dpi", inspector, StringComparison.Ordinal);
    }
}

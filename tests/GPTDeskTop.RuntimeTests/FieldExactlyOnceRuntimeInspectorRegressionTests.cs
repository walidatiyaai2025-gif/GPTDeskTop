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
    public void InspectorCapturesActualExeMonitorsBrowserAndUiTree()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");
        Assert.Contains("Environment.ProcessPath", source, StringComparison.Ordinal);
        Assert.Contains("_running", source, StringComparison.Ordinal);
        Assert.Contains("chrome", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("control.Visible", source, StringComparison.Ordinal);
        Assert.Contains("control.Enabled", source, StringComparison.Ordinal);
        Assert.Contains("control.DeviceDpi", source, StringComparison.Ordinal);
        Assert.Contains("control.Bounds", source, StringComparison.Ordinal);
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

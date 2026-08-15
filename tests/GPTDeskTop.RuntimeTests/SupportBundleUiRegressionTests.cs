namespace GPTDeskTop.RuntimeTests;

public sealed class SupportBundleUiRegressionTests
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
    public void SupportLauncherIsBoundToRuntimeHealthExpansion()
    {
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("SupportDiagnosticsControl? supportDiagnostics = null;", program, StringComparison.Ordinal);
        Assert.Contains("void EnsureSupportDiagnostics()", program, StringComparison.Ordinal);
        Assert.Contains("new SupportBundleService(chrome, monitor, database, config)", program, StringComparison.Ordinal);
        Assert.Contains("new SupportDiagnosticsControl(supportBundleService)", program, StringComparison.Ordinal);
        Assert.Contains("Visible = runtimeHealth.IsExpanded", program, StringComparison.Ordinal);
        Assert.Contains("if (runtimeHealth.IsExpanded)", program, StringComparison.Ordinal);
        Assert.Contains("EnsureSupportDiagnostics();", program, StringComparison.Ordinal);
        Assert.Contains("supportDiagnostics.Visible = runtimeHealth.IsExpanded", program, StringComparison.Ordinal);
    }

    [Fact]
    public void LauncherUsesAccessibleBusySafeSaveFlow()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SupportDiagnosticsControl.cs");

        Assert.Contains("SaveFileDialog", source, StringComparison.Ordinal);
        Assert.Contains("private bool _generating", source, StringComparison.Ordinal);
        Assert.Contains("if (_generating || IsDisposed) return", source, StringComparison.Ordinal);
        Assert.Contains("&Create Support Bundle", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Create privacy-safe support bundle\"", source, StringComparison.Ordinal);
        Assert.Contains("await _service.CreateAsync(dialog.FileName)", source, StringComparison.Ordinal);
        Assert.Contains("Privacy-safe diagnostics only", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BundleImplementationCountsOnlyStableConversationsButKeepsBroadConfigClassification()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SupportBundleService.cs");

        Assert.Contains("tabs.Count(tab => RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptTabUrl(config.Chrome.StartUrl)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs.Count(tab => RuntimeHealthPresentation.IsChatGptTabUrl(tab.Url))", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BundleImplementationCollectsOnlySanitizedRecoveryStateAndDoesNotCopySensitiveSourceFiles()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "SupportBundleService.cs");

        Assert.Contains("CollectionTimeout = TimeSpan.FromSeconds(5)", source, StringComparison.Ordinal);
        Assert.Contains("GetSavedMonitorsAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetRecentLogsAsync(500", source, StringComparison.Ordinal);
        Assert.Contains("GetSettingAsync(\"CrashRecoveryPending\"", source, StringComparison.Ordinal);
        Assert.Contains("InvalidMonitorIdentityCount", source, StringComparison.Ordinal);
        Assert.Contains("crashRecoveryPending: database.CrashRecoveryPending", source, StringComparison.Ordinal);
        Assert.Contains("invalidMonitorIdentityCount: database.InvalidMonitorIdentityCount", source, StringComparison.Ordinal);
        Assert.Contains("GetTodayLogPath", source, StringComparison.Ordinal);
        Assert.Contains("File.Move(tempPath, outputPath, overwrite: true)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.ReadAllText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.ReadAllBytes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Copy", source, StringComparison.Ordinal);
    }
}

namespace GPTDeskTop.RuntimeTests;

public sealed class CompactOperatorHeaderUiRegressionTests
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
    public void LegacyBootstrapDelegatesToTheSingleDashboardHeaderOwner()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactOperatorHeaderExperience.cs");

        Assert.Contains("CompactDashboardHeaderLayout.ApplyOpenForms()", source, StringComparison.Ordinal);
        Assert.Contains("=> CompactDashboardHeaderLayout.Apply(form);", source, StringComparison.Ordinal);
        Assert.Contains("Compatibility bootstrap retained", source, StringComparison.Ordinal);
        Assert.Contains("exactly one DPI-aware", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyHeaderOwnerCannotRestoreFiftyEightPixelLayout()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactOperatorHeaderExperience.cs");

        Assert.DoesNotContain("BeginInvoke", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Scale(root, 58)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyCompactPresentation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("chip.MinimumSize", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DpiChangedAfterParent", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityLayerRemainsPresentationOnly()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactOperatorHeaderExperience.cs");

        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeDevToolsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopMonitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Save", source, StringComparison.Ordinal);
    }
}

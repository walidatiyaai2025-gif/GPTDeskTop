namespace GPTDeskTop.RuntimeTests;

public sealed class MainDashboardExperienceRegressionTests
{
    private static string Source
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "MainDashboardExperience.cs")));

    [Fact]
    public void DashboardLayerTargetsMainFormWithoutBusinessDependencies()
    {
        var source = Source;

        Assert.Contains("if (form is MainForm)", source, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Form, DashboardRegistration>", source, StringComparison.Ordinal);
        Assert.Contains("registration.ToolTip?.Dispose()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeDevToolsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardMetricsAndToolbarKeepOperationalSemantics()
    {
        var source = Source;

        Assert.Contains("Currently running monitors", source, StringComparison.Ordinal);
        Assert.Contains("Dedicated monitor Chrome window", source, StringComparison.Ordinal);
        Assert.Contains("UpdateMetricPresentation", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.Success", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.Warning", source, StringComparison.Ordinal);
        Assert.Contains("Start All", source, StringComparison.Ordinal);
        Assert.Contains("Launch Chrome", source, StringComparison.Ordinal);
        Assert.Contains("Delete", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardKeepsSelectedMonitorAndGridGuidance()
    {
        var source = Source;

        Assert.Contains("Selected monitor summary", source, StringComparison.Ordinal);
        Assert.Contains("Edit Selected Monitor", source, StringComparison.Ordinal);
        Assert.Contains("Open ChatGPT conversations", source, StringComparison.Ordinal);
        Assert.Contains("Saved monitors", source, StringComparison.Ordinal);
        Assert.Contains("Stored activity history", source, StringComparison.Ordinal);
        Assert.Contains("DataGridViewCellBorderStyle.SingleHorizontal", source, StringComparison.Ordinal);
        Assert.Contains("ColumnHeadersHeight", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardResponsiveLayerDoesNotMutateSplitPersistenceOrRuntimeState()
    {
        var source = Source;

        Assert.Contains("ApplyResponsiveLayout", source, StringComparison.Ordinal);
        Assert.Contains("IsDashboardActionGroup(group)", source, StringComparison.Ordinal);
        Assert.Contains("actionRow.WrapContents = false", source, StringComparison.Ordinal);
        Assert.Contains("form.ClientSize.Width < 1180", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var flow in Descendants(form).OfType<FlowLayoutPanel>())", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SplitterDistance =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopMonitor", source, StringComparison.Ordinal);
    }
}

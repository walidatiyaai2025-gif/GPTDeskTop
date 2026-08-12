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
    public void CompactHeaderAppliesAfterExistingIdlePresentationPass()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactOperatorHeaderExperience.cs");

        Assert.Contains("Application.Idle += ScheduleForOpenMainForms", source, StringComparison.Ordinal);
        Assert.Contains("form.BeginInvoke(new Action(() =>", source, StringComparison.Ordinal);
        Assert.Contains("registration.Scheduled = true", source, StringComparison.Ordinal);
        Assert.Contains("if (registration.Applied || registration.Scheduled)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactHeaderReclaimsHeightWithoutDroppingLiveMetrics()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactOperatorHeaderExperience.cs");

        Assert.Contains("root.RowStyles[0].Height = Scale(root, 58);", source, StringComparison.Ordinal);
        Assert.Contains("subtitle.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("titleBlock.RowStyles[1].Height = 0", source, StringComparison.Ordinal);
        Assert.Contains("ContainsMetric(panel, \"Running\")", source, StringComparison.Ordinal);
        Assert.Contains("ContainsMetric(panel, \"Monitors\")", source, StringComparison.Ordinal);
        Assert.Contains("ContainsMetric(panel, \"Conversation tabs\")", source, StringComparison.Ordinal);
        Assert.Contains("ContainsMetric(panel, \"Chrome window\")", source, StringComparison.Ordinal);
        Assert.Contains("chip.MinimumSize = new Size(Scale(chip, 100), Scale(chip, 40));", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactHeaderIsDpiAwareAndEventDriven()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactOperatorHeaderExperience.cs");

        Assert.Contains("root.DpiChangedAfterParent +=", source, StringComparison.Ordinal);
        Assert.Contains("Math.Max(96, control.DeviceDpi) / 96d", source, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Form, Registration>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Threading.Timer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactHeaderRemainsPresentationOnly()
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

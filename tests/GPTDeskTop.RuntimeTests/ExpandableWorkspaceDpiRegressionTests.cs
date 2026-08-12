namespace GPTDeskTop.RuntimeTests;

public sealed class ExpandableWorkspaceDpiRegressionTests
{
    [Fact]
    public void ExpandableWorkspaceLayout_UsesActiveDeviceDpi()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "ExpandableWorkspaceLayout.cs");

        Assert.Contains("control.DeviceDpi", source, StringComparison.Ordinal);
        Assert.Contains("Math.Max(96, control.DeviceDpi) / 96d", source, StringComparison.Ordinal);
        Assert.Contains("control.DpiChangedAfterParent +=", source, StringComparison.Ordinal);
        Assert.Contains("control.SizeChanged +=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandableWorkspaceLayout_CoversAllExpandableDashboardSections()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "ExpandableWorkspaceLayout.cs");

        Assert.Contains("case DevelopmentTaskDashboardControl development:", source, StringComparison.Ordinal);
        Assert.Contains("case RuntimeHealthControl runtimeHealth:", source, StringComparison.Ordinal);
        Assert.Contains("case HistoryWorkspaceControl history:", source, StringComparison.Ordinal);
        Assert.Contains("DevelopmentCollapsedHeight = 72", source, StringComparison.Ordinal);
        Assert.Contains("DevelopmentExpandedHeight = 178", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthCollapsedHeight = 62", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthExpandedHeight = 188", source, StringComparison.Ordinal);
        Assert.Contains("HistoryCollapsedHeight = 56", source, StringComparison.Ordinal);
        Assert.Contains("HistoryExpandedHeight = 330", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandableWorkspaceLayout_YieldsHeightOwnershipToOnDemandHost()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "ExpandableWorkspaceLayout.cs");
        var applyStart = source.IndexOf("private static void ApplyExpandableHeight", StringComparison.Ordinal);
        var applyHeightStart = source.IndexOf("private static void ApplyHeight", applyStart, StringComparison.Ordinal);
        var apply = source[applyStart..applyHeightStart];

        Assert.Contains("UseHostManagedHeight", source, StringComparison.Ordinal);
        Assert.Contains("HostManagedControls", source, StringComparison.Ordinal);
        Assert.Contains("HostManagedControls.TryGetValue(control, out _)", apply, StringComparison.Ordinal);
        Assert.Contains("control.MinimumSize = Size.Empty", apply, StringComparison.Ordinal);
        Assert.Contains("control.MaximumSize = Size.Empty", apply, StringComparison.Ordinal);
        Assert.True(
            apply.IndexOf("HostManagedControls.TryGetValue", StringComparison.Ordinal)
            < apply.IndexOf("switch (control)", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandableWorkspaceLayout_CorrectsHeightAndMinimumWithoutBusinessMutation()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "ExpandableWorkspaceLayout.cs");

        Assert.Contains("control.MinimumSize = new Size", source, StringComparison.Ordinal);
        Assert.Contains("control.Height = expectedHeight", source, StringComparison.Ordinal);
        Assert.Contains("if (control.Height != expectedHeight)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DevelopmentTaskEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandableWorkspaceLayout_IsIncrementalRatherThanIdleRetraversal()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "ExpandableWorkspaceLayout.cs");

        Assert.Contains("if (registration.Initialized) return;", source, StringComparison.Ordinal);
        Assert.Contains("control.ControlAdded +=", source, StringComparison.Ordinal);
        Assert.Contains("if (registration.ExpandableHooked) return;", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(parts)}");
    }
}

namespace GPTDeskTop.RuntimeTests;

public sealed class CompactCanonicalNavigationRegressionTests
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
    public void ProjectsAndRuntimeInspectorStayVisibleAfterLegacyToolbarIsCollapsed()
    {
        var compact = ReadSource("src", "GPTDeskTop", "UI", "CompactTopCommandMenuExperience.cs");
        var navigation = ReadSource("src", "GPTDeskTop", "UI", "CompactCanonicalNavigationExperience.cs");

        Assert.Contains("toolbar.Visible = false", compact, StringComparison.Ordinal);
        Assert.Contains("Text, \"Projects\"", navigation, StringComparison.Ordinal);
        Assert.Contains("Text, \"Runtime Inspector\"", navigation, StringComparison.Ordinal);
        Assert.Contains("EnsureVisibleProxy(", navigation, StringComparison.Ordinal);
        Assert.Contains("text: \"Projects\"", navigation, StringComparison.Ordinal);
        Assert.Contains("text: \"Runtime Inspector\"", navigation, StringComparison.Ordinal);
        Assert.Contains("existing.Visible = true", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactNavigationReusesCanonicalButtonBehaviorInsteadOfDuplicatingWorkflows()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactCanonicalNavigationExperience.cs");

        Assert.Contains("Tag = source", source, StringComparison.Ordinal);
        Assert.Contains("InvokeExistingButton(source)", source, StringComparison.Ordinal);
        Assert.Contains("\"OnClick\"", source, StringComparison.Ordinal);
        Assert.Contains("BindingFlags.Instance | BindingFlags.NonPublic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProjectMonitorDashboardForm", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new RuntimeInspectorForm", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VisibilityInstallerDetachesAfterTheCompactMenuIsReady()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactCanonicalNavigationExperience.cs");

        Assert.Contains("Application.Idle += InstallWhenReady", source, StringComparison.Ordinal);
        Assert.Contains("Application.Idle -= InstallWhenReady", source, StringComparison.Ordinal);
        Assert.Contains("if (!TryInstall(main))", source, StringComparison.Ordinal);
    }
}

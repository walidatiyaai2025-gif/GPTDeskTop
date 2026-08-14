namespace GPTDeskTop.RuntimeTests;

public sealed class SettingsContentRenderRecoveryRegressionTests
{
    [Fact]
    public void ObsoleteLateRenderRecoveryBootstrapIsRemoved()
    {
        var path = RepositoryPath("src", "GPTDeskTop", "UI", "SettingsContentRenderRecovery.cs");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SettingsOwnsStableBusyRenderingWithoutIdleRepair()
    {
        var settings = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "UI", "SettingsForm.cs"));

        Assert.Contains("_tabs.Enabled = true;", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Idle +=", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Idle -=", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("EnabledChanged +=", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("VisibleChanged +=", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginInvoke", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Refresh()", settings, StringComparison.Ordinal);
    }

    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));
}

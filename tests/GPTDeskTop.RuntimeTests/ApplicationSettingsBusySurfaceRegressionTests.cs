namespace GPTDeskTop.RuntimeTests;

public sealed class ApplicationSettingsBusySurfaceRegressionTests
{
    [Fact]
    public void SettingsRenderRecoveryKeepsTabSurfaceEnabledDuringAsyncIo()
    {
        var recovery = ReadSource("src", "GPTDeskTop", "UI", "SettingsContentRenderRecovery.cs");

        Assert.Contains("KeepSettingsTabsEnabled", recovery, StringComparison.Ordinal);
        Assert.Contains("tabs.Enabled = true", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs.Refresh()", recovery, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}

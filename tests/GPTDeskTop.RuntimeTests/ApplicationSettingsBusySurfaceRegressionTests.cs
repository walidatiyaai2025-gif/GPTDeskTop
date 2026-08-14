namespace GPTDeskTop.RuntimeTests;

public sealed class ApplicationSettingsBusySurfaceRegressionTests
{
    [Fact]
    public void SettingsBusyStateKeepsTabSurfaceEnabledAtTheSource()
    {
        var settings = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.Contains("_tabs.Enabled = true;", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("_tabs.Enabled = !busy;", settings, StringComparison.Ordinal);
        Assert.Contains("_saveButton.Enabled = !busy;", settings, StringComparison.Ordinal);
        Assert.Contains("_exportBackupButton.Enabled = !busy;", settings, StringComparison.Ordinal);
        Assert.Contains("_importBackupButton.Enabled = !busy;", settings, StringComparison.Ordinal);
        Assert.Contains("UseWaitCursor = busy;", settings, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}

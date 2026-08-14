namespace GPTDeskTop.RuntimeTests;

public sealed class SettingsContentRenderRecoveryRegressionTests
{
    private static string Source
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "SettingsContentRenderRecovery.cs")));

    [Fact]
    public void SettingsRecoveryRunsAfterBusyToReadyAndLoadedTransitions()
    {
        var source = Source;

        Assert.Contains("Application.Idle += OnApplicationIdle", source, StringComparison.Ordinal);
        Assert.Contains("enabledTransition", source, StringComparison.Ordinal);
        Assert.Contains("loadedTransition", source, StringComparison.Ordinal);
        Assert.Contains("Settings loaded", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeAndRepaint(form, tabs)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsOwnTheirTabSurfaceInsteadOfSemanticStatusColors()
    {
        var source = Source;

        Assert.Contains("page.UseVisualStyleBackColor = false", source, StringComparison.Ordinal);
        Assert.Contains("page.BackColor = FluentTheme.Surface", source, StringComparison.Ordinal);
        Assert.Contains("layout.BackColor = FluentTheme.Surface", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.SuccessSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.InfoSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.WarningSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.DangerSubtle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryRestoresVisibilityAndForcesChildRepaintWithoutChangingBusinessSettings()
    {
        var source = Source;

        Assert.Contains("label.Visible = true", source, StringComparison.Ordinal);
        Assert.Contains("input.Visible = true", source, StringComparison.Ordinal);
        Assert.Contains("Invalidate(invalidateChildren: true)", source, StringComparison.Ordinal);
        Assert.Contains("form.Update()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveSettingsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync", source, StringComparison.Ordinal);
    }
}

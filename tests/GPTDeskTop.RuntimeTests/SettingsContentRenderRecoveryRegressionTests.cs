namespace GPTDeskTop.RuntimeTests;

public sealed class SettingsContentRenderRecoveryRegressionTests
{
    private static string Source
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "SettingsContentRenderRecovery.cs")));

    [Fact]
    public void SettingsRecoveryHooksOnceAndWaitsForLoadedState()
    {
        var source = Source;

        Assert.Contains("EnsureHooked(form)", source, StringComparison.Ordinal);
        Assert.Contains("if (state.Hooked) return;", source, StringComparison.Ordinal);
        Assert.Contains("Settings loaded", source, StringComparison.Ordinal);
        Assert.Contains("ScheduleOneShotStabilization", source, StringComparison.Ordinal);
        Assert.Contains("StabilizeLoadedSettings", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsLoadedSurfaceIsOwnedByTheSettingsForm()
    {
        var source = Source;

        Assert.Contains("tabs.Visible = true", source, StringComparison.Ordinal);
        Assert.Contains("tabs.Enabled = true", source, StringComparison.Ordinal);
        Assert.Contains("page.UseVisualStyleBackColor = false", source, StringComparison.Ordinal);
        Assert.Contains("page.BackColor = FluentTheme.Surface", source, StringComparison.Ordinal);
        Assert.Contains("layout.BackColor = FluentTheme.Surface", source, StringComparison.Ordinal);
        Assert.Contains("label.Visible = true", source, StringComparison.Ordinal);
        Assert.Contains("input.Visible = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryDoesNotRunARecurringRepaintLoopOrTouchBusinessSettings()
    {
        var source = Source;

        Assert.Contains("Exactly one invalidation after load", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeAndRepaint", source, StringComparison.Ordinal);
        Assert.DoesNotContain("form.Update()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Refresh()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Timer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveSettingsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync", source, StringComparison.Ordinal);
    }
}

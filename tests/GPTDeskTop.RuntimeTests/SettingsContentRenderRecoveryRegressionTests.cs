namespace GPTDeskTop.RuntimeTests;

public sealed class SettingsContentRenderRecoveryRegressionTests
{
    private static string Source
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "SettingsContentRenderRecovery.cs")));

    [Fact]
    public void SettingsGuardHooksEachSettingsFormOnce()
    {
        var source = Source;

        Assert.Contains("EnsureHooked(form)", source, StringComparison.Ordinal);
        Assert.Contains("if (state.Hooked) return;", source, StringComparison.Ordinal);
        Assert.Contains("tabs.EnabledChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("KeepSettingsTabsEnabled(tabs)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsTabsNeverEnterDisabledPaintingPath()
    {
        var source = Source;

        Assert.Contains("if (tabs.IsDisposed || tabs.Disposing || tabs.Enabled) return;", source, StringComparison.Ordinal);
        Assert.Contains("tabs.Enabled = true", source, StringComparison.Ordinal);
        Assert.Contains("disabled TabControl", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuardDoesNotRepaintOrTouchBusinessSettings()
    {
        var source = Source;

        Assert.DoesNotContain("Invalidate(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Update()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Refresh()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginInvoke", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Timer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveSettingsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync", source, StringComparison.Ordinal);
    }
}

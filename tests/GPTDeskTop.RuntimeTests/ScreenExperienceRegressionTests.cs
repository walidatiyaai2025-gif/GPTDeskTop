namespace GPTDeskTop.RuntimeTests;

public sealed class ScreenExperienceRegressionTests
{
    private static string Source
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "ScreenExperience.cs")));

    [Fact]
    public void ScreenExperienceBootstrapsWithoutBusinessServiceDependencies()
    {
        var source = Source;

        Assert.Contains("[ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("Application.Idle += OnApplicationIdle", source, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Form, FormRegistration>", source, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Control, ControlRegistration>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeDevToolsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatorKeyboardAcceleratorsRemainAvailable()
    {
        var source = Source;

        Assert.Contains("e.Control && e.Shift && e.KeyCode == Keys.F", source, StringComparison.Ordinal);
        Assert.Contains("e.Control && e.KeyCode == Keys.F", source, StringComparison.Ordinal);
        Assert.Contains("e.KeyCode == Keys.F5", source, StringComparison.Ordinal);
        Assert.Contains("e.Control && e.KeyCode == Keys.S", source, StringComparison.Ordinal);
        Assert.Contains("e.Control && e.KeyCode == Keys.E", source, StringComparison.Ordinal);
        Assert.Contains("e.Control && e.Shift && e.KeyCode == Keys.B", source, StringComparison.Ordinal);
        Assert.Contains("e.KeyCode == Keys.F6", source, StringComparison.Ordinal);
        Assert.Contains("e.KeyCode == Keys.Tab || e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreenExperienceKeepsSemanticStatusAndEmptyStatePresentation()
    {
        var source = Source;

        Assert.Contains("UpdateSemanticStatus", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.DangerSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.SuccessSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.InfoSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.WarningSubtle", source, StringComparison.Ordinal);
        Assert.Contains("IsEmptyStateLabel", source, StringComparison.Ordinal);
        Assert.Contains("This area currently has no matching items.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScreenExperiencePreservesLifecycleAndDynamicControlSafety()
    {
        var source = Source;

        Assert.Contains("registration.ToolTip?.Dispose()", source, StringComparison.Ordinal);
        Assert.Contains("control.ControlAdded +=", source, StringComparison.Ordinal);
        Assert.Contains("if (registration.ControlAddedHooked) return;", source, StringComparison.Ordinal);
        Assert.Contains("if (registration.TextChangedHooked)", source, StringComparison.Ordinal);
        Assert.Contains("if (registration.KeyHooked) return;", source, StringComparison.Ordinal);
        Assert.Contains("if (form.IsDisposed || form.Disposing)", source, StringComparison.Ordinal);
    }
}
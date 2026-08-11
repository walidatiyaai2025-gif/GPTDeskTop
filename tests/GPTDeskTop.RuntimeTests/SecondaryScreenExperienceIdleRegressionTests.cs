namespace GPTDeskTop.RuntimeTests;

public sealed class SecondaryScreenExperienceIdleRegressionTests
{
    private static string Source
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "SecondaryScreenExperience.cs")));

    [Fact]
    public void IdleDiscoveryAppliesEachFormExperienceOnlyOnce()
    {
        var source = Source;

        Assert.Contains("if (registration.InitialExperienceApplied) return;", source, StringComparison.Ordinal);
        Assert.Contains("registration.InitialExperienceApplied = true;", source, StringComparison.Ordinal);
        Assert.Contains("RegisterResponsive(form, () => ApplyPresentation(form));", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPresentation(form);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterResponsive(form, () => Apply(form));", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LateControlsAreRegisteredIncrementally()
    {
        var source = Source;

        Assert.Contains("RegisterDynamicTree(form, form);", source, StringComparison.Ordinal);
        Assert.Contains("root.ControlAdded +=", source, StringComparison.Ordinal);
        Assert.Contains("RegisterDynamicTree(form, e.Control);", source, StringComparison.Ordinal);
        Assert.Contains("internal bool ChildAddedHooked", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveAndStatusHooksRemainEventDriven()
    {
        var source = Source;

        Assert.Contains("control.SizeChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("form.DpiChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("label.TextChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("internal bool ResponsiveHooked", source, StringComparison.Ordinal);
        Assert.Contains("internal bool TextChangedHooked", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SpecializedPresentationRemainsPresentationOnly()
    {
        var source = Source;

        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeDevToolsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DevelopmentTaskEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveSettingsAsync", source, StringComparison.Ordinal);
    }
}

namespace GPTDeskTop.RuntimeTests;

public sealed class SecondaryScreenExperienceRegressionTests
{
    private static string Source
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "SecondaryScreenExperience.cs")));

    [Fact]
    public void SecondaryExperienceBootstrapsWithoutBusinessServiceDependencies()
    {
        var source = Source;

        Assert.Contains("[ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("Application.Idle += OnApplicationIdle", source, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Control, ControlRegistration>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeDevToolsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalPassTargetsAllSecondaryOperatorScreens()
    {
        var source = Source;

        Assert.Contains("form is SettingsForm", source, StringComparison.Ordinal);
        Assert.Contains("form is MonitorSettingsForm", source, StringComparison.Ordinal);
        Assert.Contains("case RuntimeHealthControl runtimeHealth", source, StringComparison.Ordinal);
        Assert.Contains("case HistoryWorkspaceControl history", source, StringComparison.Ordinal);
        Assert.Contains("case SupportDiagnosticsControl support", source, StringComparison.Ordinal);
        Assert.Contains("form.DpiChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("control.SizeChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("control.DeviceDpi", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeHealthAndHistoryHaveCompactLayoutContracts()
    {
        var source = Source;

        Assert.Contains("ApplyRuntimeHeaderResponsive", source, StringComparison.Ordinal);
        Assert.Contains("owner.Width < Scale(owner, 930)", source, StringComparison.Ordinal);
        Assert.Contains("owner.Width < Scale(owner, 760)", source, StringComparison.Ordinal);
        Assert.Contains("lastChecked.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("summary.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("filters.WrapContents = true", source, StringComparison.Ordinal);
        Assert.Contains("bodyLayout.RowStyles[0].SizeType = compact ? SizeType.AutoSize : SizeType.Absolute", source, StringComparison.Ordinal);
        Assert.Contains("search.Width = Scale(search, compact ? 220 : 280)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsAndMonitorFormsKeepPrimaryActionAndScrollableTabHierarchy()
    {
        var source = Source;

        Assert.Contains("FindButton(form, \"Save Settings\")", source, StringComparison.Ordinal);
        Assert.Contains("FindButton(form, \"Save Monitor\")", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.StyleButton(save, primary: true)", source, StringComparison.Ordinal);
        Assert.Contains("page.AutoScroll = true", source, StringComparison.Ordinal);
        Assert.Contains("page.AutoScrollMargin", source, StringComparison.Ordinal);
        Assert.Contains("Sensitive backup data warning", source, StringComparison.Ordinal);
        Assert.Contains("Selected monitor runtime state", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondaryStatusPresentationRemainsSemanticAndPresentationOnly()
    {
        var source = Source;

        Assert.Contains("ApplyStatusPresentation", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.DangerSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.SuccessSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.InfoSubtle", source, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.WarningSubtle", source, StringComparison.Ordinal);
        Assert.Contains("if (registration.TextChangedHooked) return;", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Click +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveSettingsAsync(", source, StringComparison.Ordinal);
    }
}
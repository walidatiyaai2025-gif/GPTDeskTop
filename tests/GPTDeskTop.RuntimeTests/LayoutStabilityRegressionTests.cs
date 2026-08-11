using GPTDeskTop.UI;

namespace GPTDeskTop.RuntimeTests;

public sealed class LayoutStabilityRegressionTests
{
    [Fact]
    public void LayoutTokens_UseSharedLogicalSpacingScale()
    {
        Assert.Equal(new[] { 4, 8, 12, 16, 24, 32 }, new[]
        {
            LayoutTokens.Space4,
            LayoutTokens.Space8,
            LayoutTokens.Space12,
            LayoutTokens.Space16,
            LayoutTokens.Space24,
            LayoutTokens.Space32
        });
    }

    [Fact]
    public void LayoutTokens_DefineUsableMinimumsAndResponsiveBreakpoints()
    {
        Assert.True(LayoutTokens.ControlHeight >= 36);
        Assert.True(LayoutTokens.MinimumUsableWidth >= 640);
        Assert.True(LayoutTokens.MinimumUsableHeight >= 480);
        Assert.True(LayoutTokens.NarrowBreakpoint < LayoutTokens.CompactBreakpoint);
        Assert.True(LayoutTokens.CompactBreakpoint < LayoutTokens.ComfortableContentWidth);
    }

    [Fact]
    public void LayoutStability_IsIncrementalInsteadOfRetraversingEveryIdle()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "LayoutStability.cs");

        Assert.Contains("if (registration.Initialized) return;", source, StringComparison.Ordinal);
        Assert.Contains("ControlAdded +=", source, StringComparison.Ordinal);
        Assert.Contains("form.Resize +=", source, StringComparison.Ordinal);
        Assert.Contains("form.DpiChanged +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.DoEvents", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutStability_CoversOverflowLongContentAndDpiScaling()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "LayoutStability.cs");

        Assert.Contains("AutoEllipsis = true", source, StringComparison.Ordinal);
        Assert.Contains("flow.WrapContents = compact", source, StringComparison.Ordinal);
        Assert.Contains("TextRenderer.MeasureText", source, StringComparison.Ordinal);
        Assert.Contains("RichTextBoxScrollBars.Both", source, StringComparison.Ordinal);
        Assert.Contains("RichTextBoxScrollBars.Vertical", source, StringComparison.Ordinal);
        Assert.Contains("ApplySplitBounds", source, StringComparison.Ordinal);
        Assert.Contains("control.DeviceDpi", source, StringComparison.Ordinal);
        Assert.Contains("page.AutoScroll = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutStability_DoesNotCompeteWithSpecializedResponsiveOwners()
    {
        var stability = ReadSource("src", "GPTDeskTop", "UI", "LayoutStability.cs");
        var secondary = ReadSource("src", "GPTDeskTop", "UI", "SecondaryScreenExperience.cs");

        Assert.Contains("if (HasSpecializedResponsiveOwner(form)) return;", stability, StringComparison.Ordinal);
        Assert.Contains("form is MainForm or SettingsForm or MonitorSettingsForm", stability, StringComparison.Ordinal);
        Assert.Contains("var compact = form.ClientSize.Width < Scale(form, 820)", secondary, StringComparison.Ordinal);
        Assert.Contains("var compact = form.ClientSize.Width < Scale(form, 800)", secondary, StringComparison.Ordinal);
        Assert.Contains("tabs.Padding = compact", secondary, StringComparison.Ordinal);
        Assert.Contains("flow.WrapContents = compact", secondary, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutStability_PreservesThePolishedDevelopmentCommandStrip()
    {
        var stability = ReadSource("src", "GPTDeskTop", "UI", "LayoutStability.cs");
        var secondary = ReadSource("src", "GPTDeskTop", "UI", "SecondaryScreenExperience.cs");

        Assert.Contains("IsSingleRowCommandFlow", stability, StringComparison.Ordinal);
        Assert.Contains("labels.Contains(\"Start\") && labels.Contains(\"Schedule\")", stability, StringComparison.Ordinal);
        Assert.Contains("actions.WrapContents = false", secondary, StringComparison.Ordinal);
        Assert.Contains("button.AutoEllipsis = false", secondary, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutStability_RemainsPresentationOnly()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "LayoutStability.cs");

        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeDevToolsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DevelopmentTaskEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveSettingsAsync", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(parts)}");
    }
}

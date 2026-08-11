using GPTDeskTop.UI;

namespace GPTDeskTop.RuntimeTests;

public sealed class LayoutStabilityRegressionTests
{
    [Fact]
    public void LayoutTokens_UseTheSharedSpacingScale()
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
    public void LayoutTokens_DefineDpiSafeMinimumsAndBreakpoints()
    {
        Assert.True(LayoutTokens.ControlHeight >= 36);
        Assert.True(LayoutTokens.MinimumUsableWidth >= 640);
        Assert.True(LayoutTokens.MinimumUsableHeight >= 480);
        Assert.True(LayoutTokens.NarrowBreakpoint < LayoutTokens.CompactBreakpoint);
        Assert.True(LayoutTokens.CompactBreakpoint < LayoutTokens.ComfortableContentWidth);
    }

    [Fact]
    public void LayoutStability_SourceContractCoversOverflowAndLongContent()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "GPTDeskTop", "UI", "LayoutStability.cs"));

        Assert.Contains("AutoEllipsis = true", source, StringComparison.Ordinal);
        Assert.Contains("WrapContents = true", source, StringComparison.Ordinal);
        Assert.Contains("AutoScaleMode.Dpi", source, StringComparison.Ordinal);
        Assert.Contains("TextRenderer.MeasureText", source, StringComparison.Ordinal);
        Assert.Contains("RichTextBoxScrollBars.Both", source, StringComparison.Ordinal);
        Assert.Contains("RichTextBoxScrollBars.Vertical", source, StringComparison.Ordinal);
        Assert.Contains("ApplySplitBounds", source, StringComparison.Ordinal);
        Assert.Contains("ControlAdded", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutStability_RemainsPresentationOnly()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "GPTDeskTop", "UI", "LayoutStability.cs"));

        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeDevToolsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DevelopmentTaskEngine", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(parts)}");
    }
}

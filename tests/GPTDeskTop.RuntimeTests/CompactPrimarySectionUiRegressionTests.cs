namespace GPTDeskTop.RuntimeTests;

public sealed class CompactPrimarySectionUiRegressionTests
{
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

    [Fact]
    public void OnlyPrimaryConversationAndMonitorSectionsAreTargeted()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactPrimarySectionExperience.cs");

        Assert.Contains("Open ChatGPT Conversations", source, StringComparison.Ordinal);
        Assert.Contains("Saved Monitors", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Live Activity\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Stored History\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Runtime Health\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SubtitleRowIsRemovedWhileGridReceivesFlexibleHeight()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactPrimarySectionExperience.cs");

        Assert.Contains("HeaderLogicalHeight = 28", source, StringComparison.Ordinal);
        Assert.Contains("section.Layout.RowStyles[1].Height = 0F", source, StringComparison.Ordinal);
        Assert.Contains("section.Layout.RowStyles[2].SizeType = SizeType.Percent", source, StringComparison.Ordinal);
        Assert.Contains("section.Layout.RowStyles[2].Height = 100F", source, StringComparison.Ordinal);
        Assert.Contains("section.Subtitle.Visible = false", source, StringComparison.Ordinal);
        Assert.Contains("section.Content.Dock = DockStyle.Fill", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GuidanceSurvivesThroughTooltipAndAccessibilityMetadata()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactPrimarySectionExperience.cs");

        Assert.Contains("_toolTip.SetToolTip(section.Title, guidance)", source, StringComparison.Ordinal);
        Assert.Contains("section.Title.AccessibleDescription = guidance", source, StringComparison.Ordinal);
        Assert.Contains("section.Title.Visible = true", source, StringComparison.Ordinal);
        Assert.Contains("section.Title.AutoEllipsis = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactHeaderIsDpiAwareAndDoesNotMutateBusinessState()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "CompactPrimarySectionExperience.cs");

        Assert.Contains("control.DeviceDpi", source, StringComparison.Ordinal);
        Assert.Contains("section.Layout.DpiChangedAfterParent += section.DpiChangedHandler", source, StringComparison.Ordinal);
        Assert.Contains("Scale(section.Layout, HeaderLogicalHeight)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalDatabase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatGptMonitorService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Click +=", source, StringComparison.Ordinal);
    }
}

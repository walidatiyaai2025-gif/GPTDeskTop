namespace GPTDeskTop.RuntimeTests;

public sealed class SingleSurfaceUiRegressionTests
{
    [Fact]
    public void OperatorWorkspaceNeverCreatesOrHidesASecondTopLevelForm()
    {
        var source = ReadUiSource("OperatorWorkspaceV2Experience.cs");

        Assert.DoesNotContain("new Form", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildLiveMonitorWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Hide();", source, StringComparison.Ordinal);
        Assert.Contains("_diagnostics.Visible = true", source, StringComparison.Ordinal);
        Assert.Contains("_root.RowStyles[3]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoUiExperienceCancelsAFormCloseJustToHideThatForm()
    {
        var uiDirectory = GetUiDirectory();
        var offenders = Directory
            .EnumerateFiles(uiDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("e.Cancel = true", StringComparison.Ordinal)
                    && source.Contains(".Hide();", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void MonitorMessageEditorsRemainTrueMultilineControls()
    {
        var monitorSource = ReadUiSource("MonitorSettingsForm.cs");
        Assert.Contains("_autoReplyBox = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = true }", monitorSource, StringComparison.Ordinal);
        Assert.Contains("_newChatMessageBox = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = true }", monitorSource, StringComparison.Ordinal);

        var guardSource = ReadUiSource("MultilineEditorExperience.cs");
        Assert.Contains("textBox.Dock = DockStyle.Fill", guardSource, StringComparison.Ordinal);
        Assert.Contains("AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right", guardSource, StringComparison.Ordinal);
        Assert.Contains("textBox.MinimumSize = new Size(0, 72)", guardSource, StringComparison.Ordinal);
        Assert.Contains("style.Height = 96F", guardSource, StringComparison.Ordinal);
    }

    private static string ReadUiSource(string fileName)
        => File.ReadAllText(Path.Combine(GetUiDirectory(), fileName));

    private static string GetUiDirectory()
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI"));
}

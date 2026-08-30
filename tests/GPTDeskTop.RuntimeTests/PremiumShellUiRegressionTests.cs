namespace GPTDeskTop.RuntimeTests;

public sealed class PremiumShellUiRegressionTests
{
    private static readonly string UiDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "UI"));

    [Fact]
    public void ShellInstallationIsIdempotentAndOwnsOneContentHost()
    {
        var source = Read("PremiumRuntimeShellExperience.cs");

        Assert.Contains("Controls.Find(\"PremiumShellRoot\", searchAllChildren: false)", source, StringComparison.Ordinal);
        Assert.Contains("Name = \"PremiumContentHost\"", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "contentHost.Controls.Add(existingSurface)"));
        Assert.DoesNotContain("new Form", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Hide();", source, StringComparison.Ordinal);

        var main = Read("MainForm.cs");
        Assert.Contains("Text = \"Dashboard\"", main, StringComparison.Ordinal);
        Assert.DoesNotContain("Text = \"GPTDeskTop\"", main, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellUsesDeterministicSplitRootAndLockedNavigationOrder()
    {
        var source = Read("PremiumRuntimeShellExperience.cs");
        Assert.Contains("Name = \"PremiumShellRoot\"", source, StringComparison.Ordinal);
        Assert.Contains("FixedPanel = FixedPanel.Panel1", source, StringComparison.Ordinal);
        Assert.Contains("IsSplitterFixed = true", source, StringComparison.Ordinal);

        var labels = new[]
        {
            "Dashboard", "Projects", "Open Conversations", "Saved Monitors",
            "Recovery / Runtime Inspector", "Development Messages",
            "GitHub / Git Settings", "Settings"
        };
        var previous = -1;
        foreach (var label in labels)
        {
            var current = source.IndexOf($"{label}\"", previous + 1, StringComparison.Ordinal);
            Assert.True(current > previous, $"Navigation item '{label}' is missing or out of order.");
            previous = current;
        }
    }

    [Fact]
    public void PremiumPaletteMatchesLockedContract()
    {
        var source = Read("FluentTheme.cs");
        foreach (var rgb in new[]
        {
            "5, 14, 24", "9, 23, 38", "12, 29, 47", "7, 20, 34",
            "16, 40, 65", "10, 113, 255", "39, 130, 255", "11, 42, 74",
            "235, 243, 255", "135, 153, 179", "28, 48, 70",
            "52, 211, 153", "245, 158, 11", "248, 81, 96", "56, 189, 248"
        })
            Assert.Contains(rgb, source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1366, 768, 96)]
    [InlineData(1600, 900, 96)]
    [InlineData(1920, 1080, 96)]
    [InlineData(1920, 1080, 120)]
    public void SupportedDesktopMatrixPreservesMinimumLogicalGeometry(int width, int height, int dpi)
    {
        var physical = new System.Drawing.Size(width, height);
        var logical = GPTDeskTop.UI.PremiumRuntimeShellExperience.CalculateLogicalViewport(physical, dpi);

        Assert.True(GPTDeskTop.UI.PremiumRuntimeShellExperience.SupportsViewport(physical, dpi));
        Assert.True(logical.Width - GPTDeskTop.UI.PremiumRuntimeShellExperience.NavigationRailWidth >= 884);
        Assert.True(logical.Height >= GPTDeskTop.UI.PremiumRuntimeShellExperience.MinimumShellHeight);
    }

    [Fact]
    public void MainDashboardReadsTheImmutableRuntimeSnapshot()
    {
        var source = Read("MainForm.cs");
        Assert.Contains("var snapshot = _monitor.GetRuntimeSnapshot();", source, StringComparison.Ordinal);
        foreach (var field in new[] { "GlobalSendState", "QueuedCount", "CurrentMonitorId", "CurrentMonitorName", "CurrentTaskState", "ChatGptState", "RateLimitActive", "NextProbeUtc", "RateLimitRemaining" })
            Assert.Contains($"snapshot.{field}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_sendQueueMetricValue.Text = _monitor.GlobalSendQueueStatus", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNamedMonitorMessageEditorHasAVisibleMultilineContract()
    {
        var monitor = Read("MonitorSettingsForm.cs");
        var create = Read("NewChatMonitorForm.cs");
        var guard = Read("MultilineEditorExperience.cs");

        foreach (var name in new[] { "_autoReplyBox", "_newChatMessageBox" })
            Assert.Contains($"{name} = new() {{ Dock = DockStyle.Fill, Multiline = true", monitor, StringComparison.Ordinal);
        foreach (var name in new[] { "_initialMessageBox", "_monitorReplyBox" })
            Assert.Contains($"private readonly TextBox {name}", create, StringComparison.Ordinal);
        Assert.Equal(2, Count(create, "MinimumSize = new Size(0, 72)"));
        Assert.Contains("form is not NewChatMonitorForm", guard, StringComparison.Ordinal);
        Assert.Contains("style.Height = 96F", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistentDashboardPathsNeverHideTopLevelForms()
    {
        var projectBootstrap = Read("ProjectMonitorUiBootstrap.cs");
        var allUi = string.Join(Environment.NewLine, Directory.EnumerateFiles(UiDirectory, "*.cs").Select(File.ReadAllText));
        Assert.DoesNotContain("_dashboardForm.Hide", projectBootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeInspectorForm.Hide", projectBootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Cancel = true", allUi.Contains(".Hide();", StringComparison.Ordinal) ? allUi : string.Empty, StringComparison.Ordinal);
    }

    private static string Read(string fileName) => File.ReadAllText(Path.Combine(UiDirectory, fileName));

    private static int Count(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;
}

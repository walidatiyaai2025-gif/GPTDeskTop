using GPTDeskTop.Configuration;
using GPTDeskTop.Data;
using GPTDeskTop.Services;
using GPTDeskTop.UI;

namespace GPTDeskTop.RuntimeTests;

public sealed class MainFormStartupRegressionTests
{
    private static readonly TimeSpan ConstructorSafetyTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void MainFormConstructorDoesNotThrowBeforeFirstLayout()
    {
        Exception? failure = null;
        var completed = false;

        var thread = new Thread(() =>
        {
            try
            {
                using var httpClient = new HttpClient();
                var database = new LocalDatabase(Path.Combine(Path.GetTempPath(), $"gptdesktop-mainform-{Guid.NewGuid():N}.db"));
                var chrome = new ChromeDevToolsService(httpClient, new ChromeConfig());
                var monitor = new ChatGptMonitorService(chrome, database, new MonitoringConfig());
                using var form = new MainForm(chrome, monitor, database);
                completed = true;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        Assert.True(thread.TrySetApartmentState(ApartmentState.STA));
        thread.Start();
        Assert.True(
            thread.Join(ConstructorSafetyTimeout),
            $"MainForm constructor did not finish within the {ConstructorSafetyTimeout.TotalSeconds:0}-second safety timeout.");
        Assert.True(completed, failure?.ToString());
        Assert.Null(failure);
    }

    [Fact]
    public void SplitterMinimumsAreDeferredUntilAFeasibleLayoutExists()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "UI", "MainForm.cs"));
        var source = File.ReadAllText(path);

        var workspaceStart = source.IndexOf("private Control BuildWorkspace()", StringComparison.Ordinal);
        var workspaceEnd = source.IndexOf("private Control BuildSelectedMonitorCard()", workspaceStart, StringComparison.Ordinal);
        var diagnosticsStart = source.IndexOf("private Control BuildDiagnostics()", StringComparison.Ordinal);
        var diagnosticsEnd = source.IndexOf("private static Control CreateActionGroup", diagnosticsStart, StringComparison.Ordinal);

        Assert.True(workspaceStart >= 0 && workspaceEnd > workspaceStart);
        Assert.True(diagnosticsStart >= 0 && diagnosticsEnd > diagnosticsStart);

        var workspace = source[workspaceStart..workspaceEnd];
        var diagnostics = source[diagnosticsStart..diagnosticsEnd];

        Assert.DoesNotContain("Panel1MinSize =", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Panel2MinSize =", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Panel1MinSize =", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("Panel2MinSize =", diagnostics, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveSplitterMinimums();", source, StringComparison.Ordinal);
        Assert.Contains("available < panel1MinSize + panel2MinSize", source, StringComparison.Ordinal);
        Assert.Contains("split.SplitterDistance = safeDistance", source, StringComparison.Ordinal);
    }
}

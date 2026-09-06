namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorOnlyRuntimeInspectorExportRegressionTests
{
    [Fact]
    public void MonitorOnlyStartupInstallsExportOnTheVisibleRuntimeInspectorCard()
    {
        var gate = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyStartupGate.cs");
        var export = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyRuntimeInspectorExport.cs");
        var monitor = ReadSource("src", "GPTDeskTop", "UI", "SimpleMonitorForm.cs");

        Assert.Contains("using var form = new SimpleMonitorForm(database);", gate, StringComparison.Ordinal);
        Assert.Contains("using var experience = MonitorOnlyExperienceController.Attach(form);", gate, StringComparison.Ordinal);
        Assert.Contains("MonitorOnlyRuntimeInspectorExport.Install(form);", gate, StringComparison.Ordinal);
        Assert.Contains("Text = \"4. Runtime Inspector — live\"", monitor, StringComparison.Ordinal);

        Assert.Contains("group.Text.Contains(\"Runtime Inspector\"", export, StringComparison.Ordinal);
        Assert.Contains("Text = \"Export\"", export, StringComparison.Ordinal);
        Assert.Contains("layout.Controls.Add(exportButton, 1, 0);", export, StringComparison.Ordinal);
        Assert.Contains("layout.SetColumnSpan(error, 2);", export, StringComparison.Ordinal);
        Assert.Contains("FluentTheme.StyleButton(exportButton, primary: true);", export, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportContainsOnlyTheLiveInspectorValuesAndWritesUtf8Text()
    {
        var export = ReadSource("src", "GPTDeskTop", "UI", "MonitorOnlyRuntimeInspectorExport.cs");

        Assert.Contains("GetField<Label>(form, \"_inspectorState\")", export, StringComparison.Ordinal);
        Assert.Contains("GetField<Label>(form, \"_inspectorMessage\")", export, StringComparison.Ordinal);
        Assert.Contains("GetField<Label>(form, \"_inspectorProgress\")", export, StringComparison.Ordinal);
        Assert.Contains("GetField<Label>(form, \"_inspectorRetries\")", export, StringComparison.Ordinal);
        Assert.Contains("GetField<Label>(form, \"_inspectorCdp\")", export, StringComparison.Ordinal);
        Assert.Contains("GetField<Label>(form, \"_inspectorError\")", export, StringComparison.Ordinal);
        Assert.Contains("state.Text,", export, StringComparison.Ordinal);
        Assert.Contains("message.Text,", export, StringComparison.Ordinal);
        Assert.Contains("progress.Text,", export, StringComparison.Ordinal);
        Assert.Contains("retries.Text,", export, StringComparison.Ordinal);
        Assert.Contains("cdp.Text,", export, StringComparison.Ordinal);
        Assert.Contains("error.Text", export, StringComparison.Ordinal);
        Assert.Contains("File.WriteAllText(dialog.FileName", export, StringComparison.Ordinal);
        Assert.Contains("new UTF8Encoding(false)", export, StringComparison.Ordinal);
        Assert.DoesNotContain("_messageEditor", export, StringComparison.Ordinal);
        Assert.DoesNotContain("GetConversationUrl", export, StringComparison.Ordinal);
        Assert.DoesNotContain("CookieContainer", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetCookies", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AuthorizationHeader", export, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GitHubToken", export, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}

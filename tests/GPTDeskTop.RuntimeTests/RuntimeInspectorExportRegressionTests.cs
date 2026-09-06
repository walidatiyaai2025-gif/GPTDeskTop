namespace GPTDeskTop.RuntimeTests;

public sealed class RuntimeInspectorExportRegressionTests
{
    [Fact]
    public void RuntimeInspectorExportsTheVisibleSanitizedSnapshotWithoutReplacingSupportBundle()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeInspectorForm.cs");

        Assert.Contains("Text = \"Export Snapshot\"", source, StringComparison.Ordinal);
        Assert.Contains("exportSnapshot.Click += (_, _) => ExportSnapshot();", source, StringComparison.Ordinal);
        Assert.Contains("actions.Controls.AddRange([refresh, copy, exportSnapshot, export]);", source, StringComparison.Ordinal);
        Assert.Contains("private void ExportSnapshot()", source, StringComparison.Ordinal);
        Assert.Contains("FileName = $\"GPTDeskTop-Runtime-Inspector-{DateTime.Now:yyyyMMdd-HHmmss}.txt\"", source, StringComparison.Ordinal);
        Assert.Contains("File.WriteAllText(dialog.FileName, _text.Text, new UTF8Encoding(false));", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeInspectorService.ToSanitizedJson(snapshot)", source, StringComparison.Ordinal);
        Assert.Contains("Text = \"Export Support Bundle\"", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeInspectorService.ExportBundle(_runtimeOwner, _monitor, dialog.FileName);", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments))));
}

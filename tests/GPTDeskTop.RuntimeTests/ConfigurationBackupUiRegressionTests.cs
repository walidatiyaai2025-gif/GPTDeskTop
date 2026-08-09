namespace GPTDeskTop.RuntimeTests;

public sealed class ConfigurationBackupUiRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void SettingsExposeAccessibleSensitiveBackupFlow()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.Contains("Backup & Portability", source, StringComparison.Ordinal);
        Assert.Contains("&Export Configuration Backup", source, StringComparison.Ordinal);
        Assert.Contains("SaveFileDialog", source, StringComparison.Ordinal);
        Assert.Contains("new ConfigurationBackupService(_database)", source, StringComparison.Ordinal);
        Assert.Contains("await service.ExportAsync(dialog.FileName)", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Export portable configuration backup\"", source, StringComparison.Ordinal);
        Assert.Contains("unlike Support Bundle", source, StringComparison.Ordinal);
        Assert.Contains("if (_busy) return", source, StringComparison.Ordinal);
        Assert.Contains("_exportBackupButton.Enabled = !busy", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportImplementationIsAllowlistedAtomicAndDoesNotReadHistory()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ConfigurationBackupService.cs");

        Assert.Contains("AllowedSettingKeys", source, StringComparison.Ordinal);
        Assert.Contains("GetSettingAsync(key, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("GetSavedMonitorsAsync", source, StringComparison.Ordinal);
        Assert.Contains("FileMode.CreateNew", source, StringComparison.Ordinal);
        Assert.Contains("File.Move(tempPath, outputPath, overwrite: true)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRecentLogsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddConversationRotationAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Copy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("monitor.TabId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("monitor.Id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("monitor.RotationCount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaDocumentsSensitiveAndExcludedState()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ConfigurationBackupService.cs");
        var docs = ReadSource("docs", "CONFIGURATION_BACKUP.md");

        Assert.Contains("SchemaVersion = \"1.0\"", source, StringComparison.Ordinal);
        Assert.Contains("full configuration backup", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime Chrome Tab IDs and SQLite monitor IDs", source, StringComparison.Ordinal);
        Assert.Contains("development-plan message catalog and schedule files", source, StringComparison.Ordinal);
        Assert.Contains("This file is **not**", docs, StringComparison.Ordinal);
        Assert.Contains("privacy-safe Support Bundle", docs, StringComparison.Ordinal);
        Assert.Contains("accepts only schema **1.0**", docs, StringComparison.Ordinal);
        Assert.Contains("one SQLite transaction", docs, StringComparison.Ordinal);
        Assert.Contains("Restart **GPTDeskTop** after import", docs, StringComparison.Ordinal);
    }
}

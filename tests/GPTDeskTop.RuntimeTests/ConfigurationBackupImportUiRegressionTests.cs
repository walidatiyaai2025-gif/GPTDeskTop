namespace GPTDeskTop.RuntimeTests;

public sealed class ConfigurationBackupImportUiRegressionTests
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
    public void SettingsExposeConfirmedBusySafeImportFlow()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");
        Assert.Contains("&Import Configuration Backup", source, StringComparison.Ordinal);
        Assert.Contains("OpenFileDialog", source, StringComparison.Ordinal);
        Assert.Contains("new ConfigurationBackupImportService(_database)", source, StringComparison.Ordinal);
        Assert.Contains("await service.LoadPlanAsync(dialog.FileName)", source, StringComparison.Ordinal);
        Assert.Contains("await service.ApplyAsync(plan)", source, StringComparison.Ordinal);
        Assert.Contains("MessageBoxButtons.YesNo", source, StringComparison.Ordinal);
        Assert.Contains("MessageBoxDefaultButton.Button2", source, StringComparison.Ordinal);
        Assert.Contains("_importBackupButton.Enabled = !busy", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Import portable configuration backup\"", source, StringComparison.Ordinal);
        Assert.Contains("Restart GPTDeskTop", source, StringComparison.Ordinal);
        Assert.Contains("Local monitors absent from the backup are not deleted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportPersistenceUsesOneTransactionAndPreservesRuntimeColumns()
    {
        var source = ReadSource("src", "GPTDeskTop", "Data", "LocalDatabase.cs");
        var start = source.IndexOf("ApplyConfigurationImportAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task DeleteMonitorAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        var importSource = source[start..end];
        Assert.Contains("connection.BeginTransaction()", importSource, StringComparison.Ordinal);
        Assert.Contains("transaction.Commit()", importSource, StringComparison.Ordinal);
        Assert.Contains("transaction.Rollback()", importSource, StringComparison.Ordinal);
        Assert.Contains("SELECT Id FROM SavedMonitors WHERE Url=$url ORDER BY Id LIMIT 2", importSource, StringComparison.Ordinal);
        Assert.Contains("more than one local monitor has that exact conversation URL", importSource, StringComparison.Ordinal);
        Assert.Contains("VALUES('', $title,$url", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM SavedMonitors", importSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TabId=$", importSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RotationCount=$", importSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportParserIsStrictAllowlistedAndNeverReadsHistoryApis()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ConfigurationBackupImportService.cs");
        Assert.Contains("JsonUnmappedMemberHandling.Disallow", source, StringComparison.Ordinal);
        Assert.Contains("AllowedSettingKeySet", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptConversationUrl", source, StringComparison.Ordinal);
        Assert.Contains("ApplyConfigurationImportAsync", source, StringComparison.Ordinal);
        Assert.Contains("MaxBackupBytes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRecentLogsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRecentLogsForMonitorAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddConversationRotationAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TabId = monitor", source, StringComparison.Ordinal);
    }
}
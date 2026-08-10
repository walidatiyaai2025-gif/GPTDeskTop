namespace GPTDeskTop.RuntimeTests;

public sealed class RetiredNoResponseSettingUiTests
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
    public void ActiveSettingsUiDoesNotExposeElapsedTimeRefreshControl()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");

        Assert.DoesNotContain("_noResponseRefresh", source, StringComparison.Ordinal);
        Assert.DoesNotContain("No-response refresh", source, StringComparison.Ordinal);
        Assert.DoesNotContain("before refreshing the monitored tab", source, StringComparison.Ordinal);
        Assert.Contains("Error-driven response waiting", source, StringComparison.Ordinal);
        Assert.Contains("Elapsed time alone never refreshes or recovers a healthy chat", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinarySettingsSaveDoesNotRewriteLegacyNoResponseKey()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SettingsForm.cs");
        var start = source.IndexOf("private async Task SaveSettingsAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task ExportConfigurationBackupAsync()", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        var saveMethod = source[start..end];
        Assert.DoesNotContain("NoResponseRefreshSeconds", saveMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyNoResponseKeyRemainsInSchemaOneBackupAllowlist()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ConfigurationBackupService.cs");

        Assert.Contains("public const string SchemaVersion = \"1.0\"", source, StringComparison.Ordinal);
        Assert.Contains("\"NoResponseRefreshSeconds\"", source, StringComparison.Ordinal);
        Assert.Contains("AllowedSettingKeys", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionMonitorStillDoesNotConsumeLegacyNoResponseKey()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");

        Assert.DoesNotContain("NoResponseRefreshSeconds", source, StringComparison.Ordinal);
        Assert.Contains("Passive long-response wait ON", source, StringComparison.Ordinal);
    }
}

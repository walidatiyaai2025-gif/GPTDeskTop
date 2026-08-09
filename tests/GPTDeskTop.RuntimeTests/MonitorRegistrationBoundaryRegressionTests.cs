namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorRegistrationBoundaryRegressionTests
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
    public void NewMonitorSaveDelegatesToSerializedRegistrationBoundary()
    {
        var source = ReadSource("src", "GPTDeskTop", "Data", "LocalDatabase.cs");

        Assert.Contains("public sealed record MonitorRegistrationResult(long MonitorId, bool Created);", source, StringComparison.Ordinal);
        Assert.Contains("RegisterMonitorIfConversationAvailableAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (monitor.Id <= 0)", source, StringComparison.Ordinal);
        Assert.Contains("return (await RegisterMonitorIfConversationAvailableAsync(monitor, cancellationToken)).MonitorId;", source, StringComparison.Ordinal);
        Assert.Contains("connection.BeginTransaction(deferred: false)", source, StringComparison.Ordinal);
        Assert.Contains("WHERE Url=$url COLLATE NOCASE", source, StringComparison.Ordinal);
        Assert.Contains("return new MonitorRegistrationResult(existingId, false);", source, StringComparison.Ordinal);
        Assert.Contains("return new MonitorRegistrationResult(monitorId, true);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE UNIQUE INDEX", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainFormKeepsFastDuplicateCheckAndAllNewSavesReachDatabaseBoundary()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");

        Assert.Contains("_monitors.FirstOrDefault(m =>", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(m.Url, selectedTab.Url, StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("await _database.SaveMonitorAsync(monitor);", source, StringComparison.Ordinal);
    }
}
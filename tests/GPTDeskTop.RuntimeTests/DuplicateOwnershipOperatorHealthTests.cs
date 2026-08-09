using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class DuplicateOwnershipOperatorHealthTests
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
    public void RuntimeHealthIsDegradedAndReportsDuplicateOwnershipCount()
    {
        var snapshot = RuntimeHealthPresentation.Create(
            chromeReachable: true,
            databaseReachable: true,
            chatGptTabCount: 1,
            savedMonitorCount: 2,
            runningMonitorCount: 0,
            checkedAt: DateTimeOffset.UtcNow,
            duplicateMonitorOwnershipCount: 2);

        Assert.Equal(RuntimeHealthLevel.Degraded, snapshot.Level);
        Assert.Equal(2, snapshot.DuplicateMonitorOwnershipCount);
        Assert.Contains("duplicate conversation ownership", snapshot.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportBundleExportsDuplicateCountWithoutConversationIdentity()
    {
        const string privateUrl = "https://chatgpt.com/c/private-duplicate-identity";
        var monitors = new[]
        {
            new SavedMonitor { Id = 101, Title = "Private Duplicate A", Url = privateUrl, Enabled = true },
            new SavedMonitor { Id = 202, Title = "Private Duplicate B", Url = privateUrl.ToUpperInvariant(), Enabled = true }
        };

        var database = SupportBundleService.CreateDatabaseSnapshot(monitors, Array.Empty<MessageLog>());
        var snapshot = new SupportBundleSnapshot(
            "1.0",
            DateTimeOffset.UtcNow,
            "test",
            ".NET",
            "Windows",
            "X64",
            "Degraded",
            "duplicate ownership",
            new SupportBundleConfigurationSnapshot("Loopback", "http", 9222, "ChatGPT", 1000, 1000, 1000, "appdata.db"),
            new SupportBundleChromeSnapshot(true, 1, 1, null),
            database,
            new SupportBundleExceptionMetadata(false, "none.log", 0, null),
            Array.Empty<string>());

        var json = SupportBundleService.SerializeSnapshot(snapshot);

        Assert.Equal(2, database.DuplicateMonitorOwnershipCount);
        Assert.Contains("DuplicateMonitorOwnershipCount", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-duplicate-identity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private Duplicate", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirectRuntimeStartChecksDuplicateOwnershipBeforeWorkerCreation()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");

        var start = source.IndexOf("public async Task StartMonitorAsync", StringComparison.Ordinal);
        var ownership = source.IndexOf("MonitorConversationOwnership.IsDuplicateOwner", start, StringComparison.Ordinal);
        var diagnostic = source.IndexOf("MonitorStartDuplicateConversationOwnership", ownership, StringComparison.Ordinal);
        var blockedReturn = source.IndexOf("return;", diagnostic, StringComparison.Ordinal);
        var worker = source.IndexOf("Task.Run(() => MonitorLoopAsync", ownership, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(ownership > start);
        Assert.True(diagnostic > ownership);
        Assert.True(blockedReturn > diagnostic);
        Assert.True(worker > blockedReturn);
    }

    [Fact]
    public void RuntimeHealthAndRecoveryRetryUseSharedDuplicateAnalyzer()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.Contains("MonitorConversationOwnership.CountDuplicateMonitors(_savedMonitors)", source, StringComparison.Ordinal);
        Assert.Contains("duplicateMonitorOwnershipCount: duplicateMonitorCount", source, StringComparison.Ordinal);
        Assert.Contains("&& duplicateMonitorCount == 0", source, StringComparison.Ordinal);
        Assert.Contains("MonitorConversationOwnership.CountDuplicateMonitors(monitors)", source, StringComparison.Ordinal);
        Assert.Contains("Duplicate conversation owners", source, StringComparison.Ordinal);
    }
}

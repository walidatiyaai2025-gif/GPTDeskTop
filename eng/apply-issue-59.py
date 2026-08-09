from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one match, found {count}")
    file.write_text(text.replace(old, new), encoding="utf-8")


replace_once(
    "src/GPTDeskTop/Services/ChatGptMonitorService.cs",
    '''    public Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab)\n    {\n        ArgumentNullException.ThrowIfNull(monitor);\n        ArgumentNullException.ThrowIfNull(tab);\n        if (monitor.Id <= 0) throw new InvalidOperationException("Save the monitor before starting it.");\n        if (string.IsNullOrWhiteSpace(monitor.AutoReply)) throw new InvalidOperationException("Auto reply text cannot be empty.");\n        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))\n            throw new InvalidOperationException("The saved monitor URL is not a stable ChatGPT conversation identity.");\n        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))\n            throw new InvalidOperationException("The selected Chrome tab is not a stable ChatGPT conversation identity.");\n        lock (_sync) { if (_running.ContainsKey(monitor.Id)) return Task.CompletedTask; var cts = new CancellationTokenSource(); var worker = Task.Run(() => MonitorLoopAsync(monitor, tab, cts.Token)); _running.Add(monitor.Id, new MonitorRuntime(cts, worker)); }\n        Activity?.Invoke(monitor.Id, $"Started: {monitor.Title}"); RunningStateChanged?.Invoke(); return Task.CompletedTask;\n    }\n''',
    '''    public async Task StartMonitorAsync(SavedMonitor monitor, ChromeTab tab)\n    {\n        ArgumentNullException.ThrowIfNull(monitor);\n        ArgumentNullException.ThrowIfNull(tab);\n        if (monitor.Id <= 0) throw new InvalidOperationException("Save the monitor before starting it.");\n        if (string.IsNullOrWhiteSpace(monitor.AutoReply)) throw new InvalidOperationException("Auto reply text cannot be empty.");\n        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))\n            throw new InvalidOperationException("The saved monitor URL is not a stable ChatGPT conversation identity.");\n        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))\n            throw new InvalidOperationException("The selected Chrome tab is not a stable ChatGPT conversation identity.");\n\n        var savedMonitors = await _database.GetSavedMonitorsAsync();\n        if (MonitorConversationOwnership.IsDuplicateOwner(monitor.Id, savedMonitors))\n        {\n            const string message = "Saved monitor conversation ownership is ambiguous. Resolve duplicate monitor rows before starting this monitor.";\n            await _database.AddLogAsync(\n                "System",\n                string.Empty,\n                message,\n                "MonitorStartDuplicateConversationOwnership",\n                monitor.Id,\n                monitor.TabId,\n                monitor.Title);\n            HistoryChanged?.Invoke();\n            Activity?.Invoke(monitor.Id, message);\n            return;\n        }\n\n        lock (_sync)\n        {\n            if (_running.ContainsKey(monitor.Id)) return;\n            var cts = new CancellationTokenSource();\n            var worker = Task.Run(() => MonitorLoopAsync(monitor, tab, cts.Token));\n            _running.Add(monitor.Id, new MonitorRuntime(cts, worker));\n        }\n        Activity?.Invoke(monitor.Id, $"Started: {monitor.Title}");\n        RunningStateChanged?.Invoke();\n    }\n''')

replace_once(
    "src/GPTDeskTop/Services/RuntimeHealthPresentation.cs",
    '''    public bool CrashRecoveryPending { get; init; }\n    public int InvalidMonitorIdentityCount { get; init; }\n''',
    '''    public bool CrashRecoveryPending { get; init; }\n    public int InvalidMonitorIdentityCount { get; init; }\n    public int DuplicateMonitorOwnershipCount { get; init; }\n''')

replace_once(
    "src/GPTDeskTop/Services/RuntimeHealthPresentation.cs",
    '''        string? databaseError = null,\n        bool crashRecoveryPending = false,\n        int invalidMonitorIdentityCount = 0)\n''',
    '''        string? databaseError = null,\n        bool crashRecoveryPending = false,\n        int invalidMonitorIdentityCount = 0,\n        int duplicateMonitorOwnershipCount = 0)\n''')

replace_once(
    "src/GPTDeskTop/Services/RuntimeHealthPresentation.cs",
    '''        runningMonitorCount = Math.Clamp(runningMonitorCount, 0, savedMonitorCount);\n        invalidMonitorIdentityCount = Math.Clamp(invalidMonitorIdentityCount, 0, savedMonitorCount);\n''',
    '''        runningMonitorCount = Math.Clamp(runningMonitorCount, 0, savedMonitorCount);\n        invalidMonitorIdentityCount = Math.Clamp(invalidMonitorIdentityCount, 0, savedMonitorCount);\n        duplicateMonitorOwnershipCount = Math.Clamp(duplicateMonitorOwnershipCount, 0, savedMonitorCount);\n''')

replace_once(
    "src/GPTDeskTop/Services/RuntimeHealthPresentation.cs",
    '''        else if (crashRecoveryPending)\n        {\n            level = RuntimeHealthLevel.Degraded;\n            summary = "Crash recovery has unresolved work pending.";\n        }\n''',
    '''        else if (duplicateMonitorOwnershipCount > 0)\n        {\n            level = RuntimeHealthLevel.Degraded;\n            summary = duplicateMonitorOwnershipCount == 1\n                ? "Runtime automation is blocked by 1 saved monitor with duplicate conversation ownership."\n                : $"Runtime automation is blocked by {duplicateMonitorOwnershipCount} saved monitors with duplicate conversation ownership.";\n        }\n        else if (crashRecoveryPending)\n        {\n            level = RuntimeHealthLevel.Degraded;\n            summary = "Crash recovery has unresolved work pending.";\n        }\n''')

replace_once(
    "src/GPTDeskTop/Services/RuntimeHealthPresentation.cs",
    '''            CrashRecoveryPending = crashRecoveryPending,\n            InvalidMonitorIdentityCount = invalidMonitorIdentityCount\n''',
    '''            CrashRecoveryPending = crashRecoveryPending,\n            InvalidMonitorIdentityCount = invalidMonitorIdentityCount,\n            DuplicateMonitorOwnershipCount = duplicateMonitorOwnershipCount\n''')

replace_once(
    "src/GPTDeskTop/UI/RuntimeHealthControl.cs",
    '''        AccessibleDescription = "Shows Chrome DevTools, SQLite, ChatGPT conversation, saved monitor and crash recovery health without changing runtime state during health probes.";\n''',
    '''        AccessibleDescription = "Shows Chrome DevTools, SQLite, ChatGPT conversation, saved monitor, duplicate ownership and crash recovery health without changing runtime state during health probes.";\n''')

replace_once(
    "src/GPTDeskTop/UI/RuntimeHealthControl.cs",
    '''        _toolTip.SetToolTip(_recoveryValue, "Shows whether crash recovery is clear, pending, or blocked by invalid saved conversation identities.");\n''',
    '''        _toolTip.SetToolTip(_recoveryValue, "Shows whether crash recovery is clear, pending, or blocked by invalid identities or duplicate conversation ownership.");\n''')

replace_once(
    "src/GPTDeskTop/UI/RuntimeHealthControl.cs",
    '''            var runningMonitors = _savedMonitors.Count(monitor => _monitor.IsMonitorRunning(monitor.Id));\n            var invalidMonitorCount = _savedMonitors.Count(monitor => !RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url));\n            var recoveryPending = databaseProbe.Value?.CrashRecoveryPending == true;\n''',
    '''            var runningMonitors = _savedMonitors.Count(monitor => _monitor.IsMonitorRunning(monitor.Id));\n            var invalidMonitorCount = _savedMonitors.Count(monitor => !RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url));\n            var duplicateMonitorCount = MonitorConversationOwnership.CountDuplicateMonitors(_savedMonitors);\n            var recoveryPending = databaseProbe.Value?.CrashRecoveryPending == true;\n''')

replace_once(
    "src/GPTDeskTop/UI/RuntimeHealthControl.cs",
    '''                databaseProbe.Error,\n                crashRecoveryPending: recoveryPending,\n                invalidMonitorIdentityCount: invalidMonitorCount);\n''',
    '''                databaseProbe.Error,\n                crashRecoveryPending: recoveryPending,\n                invalidMonitorIdentityCount: invalidMonitorCount,\n                duplicateMonitorOwnershipCount: duplicateMonitorCount);\n''')

replace_once(
    "src/GPTDeskTop/UI/RuntimeHealthControl.cs",
    '''                && recoveryPending\n                && invalidMonitorCount == 0;\n''',
    '''                && recoveryPending\n                && invalidMonitorCount == 0\n                && duplicateMonitorCount == 0;\n''')

replace_once(
    "src/GPTDeskTop/UI/RuntimeHealthControl.cs",
    '''        var recoveryText = snapshot.InvalidMonitorIdentityCount > 0\n            ? $"Blocked ({snapshot.InvalidMonitorIdentityCount})"\n            : snapshot.CrashRecoveryPending ? "Pending" : "Clear";\n        var recoveryColor = snapshot.InvalidMonitorIdentityCount > 0 || snapshot.CrashRecoveryPending\n            ? FluentTheme.Warning\n            : FluentTheme.Success;\n        SetMetric(\n            _recoveryValue,\n            snapshot.DatabaseReachable ? recoveryText : "—",\n            snapshot.DatabaseReachable ? recoveryColor : FluentTheme.Muted,\n            snapshot.InvalidMonitorIdentityCount > 0\n                ? "One or more saved monitors need a stable ChatGPT conversation rebind before crash recovery can complete."\n                : snapshot.CrashRecoveryPending\n                    ? "Crash recovery still has unresolved work pending."\n                    : "No pending crash recovery blocker is recorded.");\n''',
    '''        var hasRecoveryBlocker = snapshot.InvalidMonitorIdentityCount > 0 || snapshot.DuplicateMonitorOwnershipCount > 0;\n        var recoveryText = hasRecoveryBlocker\n            ? $"Blocked (I{snapshot.InvalidMonitorIdentityCount} / D{snapshot.DuplicateMonitorOwnershipCount})"\n            : snapshot.CrashRecoveryPending ? "Pending" : "Clear";\n        var recoveryColor = hasRecoveryBlocker || snapshot.CrashRecoveryPending\n            ? FluentTheme.Warning\n            : FluentTheme.Success;\n        SetMetric(\n            _recoveryValue,\n            snapshot.DatabaseReachable ? recoveryText : "—",\n            snapshot.DatabaseReachable ? recoveryColor : FluentTheme.Muted,\n            hasRecoveryBlocker\n                ? $"Invalid conversation identities: {snapshot.InvalidMonitorIdentityCount}. Duplicate conversation owners: {snapshot.DuplicateMonitorOwnershipCount}."\n                : snapshot.CrashRecoveryPending\n                    ? "Crash recovery still has unresolved work pending."\n                    : "No pending crash recovery blocker is recorded.");\n''')

replace_once(
    "src/GPTDeskTop/UI/RuntimeHealthControl.cs",
    '''        if (monitors.Any(saved => !RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url)))\n        {\n            MessageBox.Show(\n                FindForm(),\n                "Crash recovery is still blocked by an invalid saved monitor identity. Use Repair… first.",\n                "Recovery Blocked",\n                MessageBoxButtons.OK,\n                MessageBoxIcon.Warning);\n            await RefreshAsync();\n            return;\n        }\n\n        var confirmation = "Retry pending crash recovery now?\\n\\nThis uses the non-destructive PendingRetry path. It may send the configured recovery message only to unresolved monitors; monitors with persisted success receipts are not sent again.";\n''',
    '''        if (monitors.Any(saved => !RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url)))\n        {\n            MessageBox.Show(\n                FindForm(),\n                "Crash recovery is still blocked by an invalid saved monitor identity. Use Repair… first.",\n                "Recovery Blocked",\n                MessageBoxButtons.OK,\n                MessageBoxIcon.Warning);\n            await RefreshAsync();\n            return;\n        }\n\n        var duplicateMonitorCount = MonitorConversationOwnership.CountDuplicateMonitors(monitors);\n        if (duplicateMonitorCount > 0)\n        {\n            MessageBox.Show(\n                FindForm(),\n                $"Crash recovery is blocked by duplicate ownership on {duplicateMonitorCount} saved monitor row(s). Remove or rebind the duplicate monitor rows before retrying.",\n                "Recovery Blocked",\n                MessageBoxButtons.OK,\n                MessageBoxIcon.Warning);\n            await RefreshAsync();\n            return;\n        }\n\n        var confirmation = "Retry pending crash recovery now?\\n\\nThis uses the non-destructive PendingRetry path. It may send the configured recovery message only to unresolved monitors; monitors with persisted success receipts are not sent again.";\n''')

replace_once(
    "src/GPTDeskTop/Services/SupportBundleService.cs",
    '''    public bool CrashRecoveryPending { get; init; }\n    public int InvalidMonitorIdentityCount { get; init; }\n''',
    '''    public bool CrashRecoveryPending { get; init; }\n    public int InvalidMonitorIdentityCount { get; init; }\n    public int DuplicateMonitorOwnershipCount { get; init; }\n''')

replace_once(
    "src/GPTDeskTop/Services/SupportBundleService.cs",
    '''            database.FailureType,\n            crashRecoveryPending: database.CrashRecoveryPending,\n            invalidMonitorIdentityCount: database.InvalidMonitorIdentityCount);\n''',
    '''            database.FailureType,\n            crashRecoveryPending: database.CrashRecoveryPending,\n            invalidMonitorIdentityCount: database.InvalidMonitorIdentityCount,\n            duplicateMonitorOwnershipCount: database.DuplicateMonitorOwnershipCount);\n''')

replace_once(
    "src/GPTDeskTop/Services/SupportBundleService.cs",
    '''            CrashRecoveryPending = crashRecoveryPending,\n            InvalidMonitorIdentityCount = monitorList.Count(monitor =>\n                !RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))\n''',
    '''            CrashRecoveryPending = crashRecoveryPending,\n            InvalidMonitorIdentityCount = monitorList.Count(monitor =>\n                !RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url)),\n            DuplicateMonitorOwnershipCount = MonitorConversationOwnership.CountDuplicateMonitors(monitorList)\n''')

Path("tests/GPTDeskTop.RuntimeTests/DuplicateOwnershipOperatorHealthTests.cs").write_text(r'''using GPTDeskTop.Models;
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
        Assert.DoesNotContain("101", json, StringComparison.Ordinal);
        Assert.DoesNotContain("202", json, StringComparison.Ordinal);
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
''', encoding="utf-8")

print("Issue #59 patch applied.")

from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one match, found {count}")
    file.write_text(text.replace(old, new), encoding="utf-8")


Path("src/GPTDeskTop/Services/DuplicateOwnershipRepairService.cs").write_text(r'''using GPTDeskTop.Data;
using GPTDeskTop.Models;

namespace GPTDeskTop.Services;

/// <summary>
/// Repairs legacy duplicate stable-conversation ownership by moving exactly one
/// duplicate owner to a different stable conversation that is not owned by any
/// other saved monitor. The saved monitor row is updated in place so its local
/// identity, history relationship, operator configuration and rotation state are
/// preserved.
/// </summary>
public sealed class DuplicateOwnershipRepairService
{
    private readonly LocalDatabase _database;

    public DuplicateOwnershipRepairService(LocalDatabase database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<MonitorIdentityRebindResult> RebindAsync(
        long monitorId,
        ChromeTab targetTab,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetTab);
        if (monitorId <= 0)
            throw new ArgumentOutOfRangeException(nameof(monitorId));
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(targetTab.Url))
            throw new InvalidOperationException("The selected Chrome tab is not a stable ChatGPT conversation identity.");
        if (string.IsNullOrWhiteSpace(targetTab.Id))
            throw new InvalidOperationException("The selected ChatGPT conversation does not have a usable Chrome target ID.");

        var monitors = await _database.GetSavedMonitorsAsync(cancellationToken);
        var monitor = monitors.SingleOrDefault(saved => saved.Id == monitorId)
            ?? throw new InvalidOperationException($"Saved monitor #{monitorId} no longer exists.");

        if (!MonitorConversationOwnership.IsDuplicateOwner(monitor.Id, monitors))
            throw new InvalidOperationException("This monitor is not currently part of duplicate ChatGPT conversation ownership.");

        if (string.Equals(monitor.Url, targetTab.Url, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a different unowned ChatGPT conversation to resolve duplicate ownership.");

        var existingOwner = monitors.FirstOrDefault(saved =>
            saved.Id != monitor.Id
            && string.Equals(saved.Url, targetTab.Url, StringComparison.OrdinalIgnoreCase));
        if (existingOwner is not null)
            throw new InvalidOperationException($"Monitor #{existingOwner.Id} already owns the selected ChatGPT conversation.");

        var previousUrl = monitor.Url ?? string.Empty;
        monitor.TabId = targetTab.Id;
        monitor.Title = string.IsNullOrWhiteSpace(targetTab.Title) ? monitor.Title : targetTab.Title;
        monitor.Url = targetTab.Url;

        await _database.SaveMonitorAsync(monitor, cancellationToken);
        await _database.AddLogAsync(
            "System",
            "Monitor ownership repair",
            $"Rebound duplicate owner monitor #{monitor.Id} to a different unowned stable ChatGPT conversation.",
            "MonitorDuplicateConversationOwnershipRebound",
            monitor.Id,
            monitor.TabId,
            monitor.Title,
            cancellationToken);

        var pending = string.Equals(
            await _database.GetSettingAsync("CrashRecoveryPending", cancellationToken),
            "1",
            StringComparison.Ordinal);

        return new MonitorIdentityRebindResult(monitor.Id, previousUrl, monitor.Url, pending);
    }
}
''', encoding="utf-8")

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''    private readonly MonitorIdentityRepairService _repairService;\n''',
    '''    private readonly MonitorIdentityRepairService _repairService;\n    private readonly DuplicateOwnershipRepairService _duplicateRepairService;\n''')

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''        _repairService = new MonitorIdentityRepairService(database);\n\n        Text = "Repair Monitor Conversation Identity";\n''',
    '''        _repairService = new MonitorIdentityRepairService(database);\n        _duplicateRepairService = new DuplicateOwnershipRepairService(database);\n\n        Text = "Repair Monitor Conversation Ownership";\n''')

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''        AccessibleName = "Monitor conversation identity repair";\n        AccessibleDescription = "Rebind an invalid legacy saved monitor to an open stable ChatGPT conversation while preserving the same monitor identity and history.";\n''',
    '''        AccessibleName = "Monitor conversation blocker repair";\n        AccessibleDescription = "Repair an invalid saved conversation identity or move one duplicate owner to an unowned stable ChatGPT conversation while preserving the same monitor identity and history.";\n''')

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''            Text = "Repair a recovery blocker",\n''',
    '''            Text = "Repair a conversation blocker",\n''')

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''        heading.Controls.Add(FluentTheme.CreateMutedLabel("Choose an invalid saved monitor and the open ChatGPT conversation it should track. The existing Monitor ID, history, settings and rotation count are preserved."), 0, 1);\n\n        root.Controls.Add(heading, 0, 0);\n        root.Controls.Add(BuildSelectionCard("Invalid saved monitor", "Only monitors whose saved URL is not a stable /c/{conversation-id} identity are listed.", _monitorBox), 0, 1);\n        root.Controls.Add(BuildSelectionCard("Open replacement conversation", "Only stable ChatGPT conversation tabs visible through the dedicated Chrome/CDP session are listed.", _conversationBox), 0, 2);\n''',
    '''        heading.Controls.Add(FluentTheme.CreateMutedLabel("Choose an invalid identity or duplicate owner and a safe replacement conversation. The existing Monitor ID, history, settings and rotation count are preserved."), 0, 1);\n\n        root.Controls.Add(heading, 0, 0);\n        root.Controls.Add(BuildSelectionCard("Blocked saved monitor", "Invalid identities and every monitor participating in duplicate stable-conversation ownership are listed.", _monitorBox), 0, 1);\n        root.Controls.Add(BuildSelectionCard("Unowned replacement conversation", "Only stable ChatGPT conversations that are not currently owned by any saved monitor are offered.", _conversationBox), 0, 2);\n''')

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''        _statusLabel.Text = "Refresh to discover recovery blockers and open ChatGPT conversations. Rebinding does not clear CrashRecoveryPending directly; recovery receipts remain authoritative.";\n''',
    '''        _statusLabel.Text = "Refresh to discover invalid identities, duplicate owners and safe unowned replacement conversations. Rebinding never clears CrashRecoveryPending directly.";\n''')

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''        _monitorBox.AccessibleName = "Invalid saved monitor";\n        _monitorBox.AccessibleDescription = "Select the legacy monitor whose conversation identity needs repair.";\n        _conversationBox.AccessibleName = "Replacement ChatGPT conversation";\n        _conversationBox.AccessibleDescription = "Select the currently open stable ChatGPT conversation to bind to the existing monitor.";\n''',
    '''        _monitorBox.AccessibleName = "Blocked saved monitor";\n        _monitorBox.AccessibleDescription = "Select an invalid identity or duplicate conversation owner that needs repair.";\n        _conversationBox.AccessibleName = "Unowned replacement ChatGPT conversation";\n        _conversationBox.AccessibleDescription = "Select a currently open stable ChatGPT conversation that no saved monitor owns.";\n''')

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''            var monitors = await _database.GetSavedMonitorsAsync(timeout.Token);\n            var invalidMonitors = monitors\n                .Where(saved => !RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url))\n                .Select(saved => new MonitorChoice(saved))\n                .ToList();\n\n            var tabs = await _chrome.GetTabsAsync(timeout.Token);\n            var conversations = tabs\n                .Where(tab => RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))\n                .GroupBy(tab => tab.Url, StringComparer.OrdinalIgnoreCase)\n                .Select(group => group.First())\n                .Select(tab => new ConversationChoice(tab))\n                .ToList();\n\n            _monitorBox.DataSource = invalidMonitors;\n            _conversationBox.DataSource = conversations;\n            _statusLabel.Text = invalidMonitors.Count == 0\n                ? "No invalid saved monitor identities were found."\n                : conversations.Count == 0\n                    ? $"{invalidMonitors.Count} monitor(s) need rebind, but no stable ChatGPT conversation is currently open."\n                    : $"{invalidMonitors.Count} monitor(s) need rebind. Select a monitor and replacement conversation.";\n''',
    '''            var monitors = await _database.GetSavedMonitorsAsync(timeout.Token);\n            var duplicateIds = MonitorConversationOwnership.FindDuplicateMonitorIds(monitors);\n            var blockers = monitors\n                .Where(saved => !RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url) || duplicateIds.Contains(saved.Id))\n                .Select(saved => new MonitorChoice(saved, duplicateIds.Contains(saved.Id)))\n                .ToList();\n            var invalidCount = blockers.Count(choice => !choice.IsDuplicateOwner);\n            var duplicateCount = blockers.Count(choice => choice.IsDuplicateOwner);\n            var ownedConversationUrls = monitors\n                .Where(saved => RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url))\n                .Select(saved => saved.Url)\n                .ToHashSet(StringComparer.OrdinalIgnoreCase);\n\n            var tabs = await _chrome.GetTabsAsync(timeout.Token);\n            var conversations = tabs\n                .Where(tab => RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))\n                .Where(tab => !ownedConversationUrls.Contains(tab.Url))\n                .GroupBy(tab => tab.Url, StringComparer.OrdinalIgnoreCase)\n                .Select(group => group.First())\n                .Select(tab => new ConversationChoice(tab))\n                .ToList();\n\n            _monitorBox.DataSource = blockers;\n            _conversationBox.DataSource = conversations;\n            _statusLabel.Text = blockers.Count == 0\n                ? "No invalid identities or duplicate conversation owners were found."\n                : conversations.Count == 0\n                    ? $"{invalidCount} invalid identity blocker(s) and {duplicateCount} duplicate-owner blocker(s) were found, but no unowned stable ChatGPT conversation is currently open."\n                    : $"{invalidCount} invalid identity blocker(s) and {duplicateCount} duplicate-owner blocker(s) can be repaired. Select a monitor and unowned replacement conversation.";\n''')

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''        var message = $"Rebind monitor #{monitorChoice.Monitor.Id} to this ChatGPT conversation?{Environment.NewLine}{Environment.NewLine}{conversationChoice.Tab.Title}{Environment.NewLine}{conversationChoice.Tab.Url}{Environment.NewLine}{Environment.NewLine}The monitor ID, history, automation settings and rotation count will be preserved.";\n''',
    '''        var blockerKind = monitorChoice.IsDuplicateOwner ? "duplicate owner" : "invalid identity";\n        var message = $"Rebind {blockerKind} monitor #{monitorChoice.Monitor.Id} to this unowned ChatGPT conversation?{Environment.NewLine}{Environment.NewLine}{conversationChoice.Tab.Title}{Environment.NewLine}{conversationChoice.Tab.Url}{Environment.NewLine}{Environment.NewLine}The monitor ID, history, automation settings and rotation count will be preserved.";\n''')

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''            var result = await _repairService.RebindAsync(monitorChoice.Monitor.Id, conversationChoice.Tab);\n''',
    '''            var result = monitorChoice.IsDuplicateOwner\n                ? await _duplicateRepairService.RebindAsync(monitorChoice.Monitor.Id, conversationChoice.Tab)\n                : await _repairService.RebindAsync(monitorChoice.Monitor.Id, conversationChoice.Tab);\n''')

replace_once(
    "src/GPTDeskTop/UI/MonitorIdentityRepairForm.cs",
    '''    private sealed record MonitorChoice(SavedMonitor Monitor)\n    {\n        public override string ToString() => $"#{Monitor.Id}  {Monitor.Title}  —  {Monitor.Url}";\n    }\n''',
    '''    private sealed record MonitorChoice(SavedMonitor Monitor, bool IsDuplicateOwner)\n    {\n        public override string ToString()\n            => $"{(IsDuplicateOwner ? "Duplicate owner" : "Invalid identity")}  —  #{Monitor.Id}  {Monitor.Title}  —  {Monitor.Url}";\n    }\n''')

replace_once(
    "src/GPTDeskTop/UI/RuntimeHealthControl.cs",
    '''            _repairButton.Enabled = chromeProbe.Succeeded && databaseProbe.Succeeded && invalidMonitorCount > 0;\n''',
    '''            _repairButton.Enabled = chromeProbe.Succeeded\n                && databaseProbe.Succeeded\n                && (invalidMonitorCount > 0 || duplicateMonitorCount > 0);\n''')

replace_once(
    "src/GPTDeskTop/UI/RuntimeHealthControl.cs",
    '''                $"Crash recovery is blocked by duplicate ownership on {duplicateMonitorCount} saved monitor row(s). Remove or rebind the duplicate monitor rows before retrying.",\n''',
    '''                $"Crash recovery is blocked by duplicate ownership on {duplicateMonitorCount} saved monitor row(s). Use Repair… to move a duplicate owner to an unowned stable conversation before retrying.",\n''')

Path("tests/GPTDeskTop.RuntimeTests/DuplicateOwnershipRepairServiceTests.cs").write_text(r'''using GPTDeskTop.Data;
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class DuplicateOwnershipRepairServiceTests
{
    [Fact]
    public async Task RebindDuplicateOwnerPreservesIdentityConfigurationHistoryAndPendingRecovery()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            await database.SetSettingAsync("CrashRecoveryPending", "1");

            const string duplicateUrl = "https://chatgpt.com/c/legacy-duplicate";
            var firstId = await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "owner-a",
                Title = "Owner A",
                Url = duplicateUrl
            });

            var duplicate = new SavedMonitor
            {
                TabId = "owner-b-old",
                Title = "Owner B",
                Url = "https://chatgpt.com/c/temporary-unique",
                AutoReply = "continue duplicate safely",
                ReplyDelaySeconds = 11,
                TimerSeconds = 5,
                Enabled = false,
                ConversationRotationEnabled = true,
                NewChatStartMessage = "resume duplicate",
                NewChatDelaySeconds = 45,
                RotationCooldownSeconds = 78,
                MaxConversationRotations = 13,
                RotationCount = 8,
                ModelRoutingEnabled = true,
                PreferredModel = "GPT-5",
                FallbackModel = "Auto"
            };
            var duplicateId = await database.SaveMonitorAsync(duplicate);
            duplicate.Url = duplicateUrl;
            await database.SaveMonitorAsync(duplicate);
            await database.AddLogAsync("Inbound", "before-duplicate", "history", "Detected", duplicateId, "owner-b-old", "Owner B");

            var before = await database.GetSavedMonitorsAsync();
            Assert.Equal(2, MonitorConversationOwnership.CountDuplicateMonitors(before));
            Assert.True(MonitorConversationOwnership.IsDuplicateOwner(firstId, before));
            Assert.True(MonitorConversationOwnership.IsDuplicateOwner(duplicateId, before));

            var service = new DuplicateOwnershipRepairService(database);
            var result = await service.RebindAsync(
                duplicateId,
                new ChromeTab
                {
                    Id = "owner-b-new",
                    Title = "Owner B replacement",
                    Url = "https://chatgpt.com/c/unowned-replacement"
                });

            Assert.Equal(duplicateId, result.MonitorId);
            Assert.Equal(duplicateUrl, result.PreviousUrl);
            Assert.Equal("https://chatgpt.com/c/unowned-replacement", result.NewUrl);
            Assert.True(result.CrashRecoveryPending);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));

            var savedMonitors = await database.GetSavedMonitorsAsync();
            Assert.Equal(0, MonitorConversationOwnership.CountDuplicateMonitors(savedMonitors));
            var saved = Assert.Single(savedMonitors.Where(monitor => monitor.Id == duplicateId));
            Assert.Equal("owner-b-new", saved.TabId);
            Assert.Equal("Owner B replacement", saved.Title);
            Assert.Equal("https://chatgpt.com/c/unowned-replacement", saved.Url);
            Assert.Equal("continue duplicate safely", saved.AutoReply);
            Assert.Equal(11, saved.ReplyDelaySeconds);
            Assert.Equal(5, saved.TimerSeconds);
            Assert.False(saved.Enabled);
            Assert.True(saved.ConversationRotationEnabled);
            Assert.Equal("resume duplicate", saved.NewChatStartMessage);
            Assert.Equal(45, saved.NewChatDelaySeconds);
            Assert.Equal(78, saved.RotationCooldownSeconds);
            Assert.Equal(13, saved.MaxConversationRotations);
            Assert.Equal(8, saved.RotationCount);
            Assert.True(saved.ModelRoutingEnabled);
            Assert.Equal("GPT-5", saved.PreferredModel);
            Assert.Equal("Auto", saved.FallbackModel);

            var history = await database.GetRecentLogsForMonitorAsync(duplicateId, 10);
            Assert.Equal(2, history.Count);
            Assert.Contains(history, log => log.Prompt == "before-duplicate" && log.Response == "history");
            Assert.Contains(history, log => log.Status == "MonitorDuplicateConversationOwnershipRebound" && log.TabId == "owner-b-new");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task RebindRejectsUniqueOwnerAndConversationOwnedByAnotherMonitor()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();

            var uniqueId = await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "unique",
                Title = "Unique",
                Url = "https://chatgpt.com/c/unique-source"
            });
            var service = new DuplicateOwnershipRepairService(database);
            var uniqueError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                uniqueId,
                new ChromeTab { Id = "free", Title = "Free", Url = "https://chatgpt.com/c/free-target" }));
            Assert.Contains("not currently part of duplicate", uniqueError.Message, StringComparison.OrdinalIgnoreCase);

            const string duplicateUrl = "https://chatgpt.com/c/duplicate-source";
            var first = new SavedMonitor { TabId = "a", Title = "A", Url = duplicateUrl };
            var firstId = await database.SaveMonitorAsync(first);
            var second = new SavedMonitor { TabId = "b", Title = "B", Url = "https://chatgpt.com/c/temporary-b" };
            var secondId = await database.SaveMonitorAsync(second);
            second.Url = duplicateUrl;
            await database.SaveMonitorAsync(second);

            var ownedTargetId = await database.SaveMonitorAsync(new SavedMonitor
            {
                TabId = "target-owner",
                Title = "Target owner",
                Url = "https://chatgpt.com/c/already-owned-target"
            });

            var ownedError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                secondId,
                new ChromeTab { Id = "target", Title = "Target", Url = "https://chatgpt.com/c/already-owned-target" }));
            Assert.Contains($"Monitor #{ownedTargetId} already owns", ownedError.Message, StringComparison.OrdinalIgnoreCase);

            var sameError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                firstId,
                new ChromeTab { Id = "same", Title = "Same", Url = duplicateUrl }));
            Assert.Contains("different unowned", sameError.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task RebindRejectsInvalidTargetWithoutMutatingDuplicateOwner()
    {
        var root = CreateTempRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "test.db"));
            await database.InitializeAsync();
            const string duplicateUrl = "https://chatgpt.com/c/duplicate-invalid-target";
            var first = new SavedMonitor { TabId = "a", Title = "A", Url = duplicateUrl };
            await database.SaveMonitorAsync(first);
            var second = new SavedMonitor { TabId = "b", Title = "B", Url = "https://chatgpt.com/c/temporary" };
            var secondId = await database.SaveMonitorAsync(second);
            second.Url = duplicateUrl;
            await database.SaveMonitorAsync(second);

            var service = new DuplicateOwnershipRepairService(database);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RebindAsync(
                secondId,
                new ChromeTab { Id = "home", Title = "Home", Url = "https://chatgpt.com/" }));

            Assert.Contains("not a stable ChatGPT conversation", error.Message, StringComparison.OrdinalIgnoreCase);
            var unchanged = Assert.Single((await database.GetSavedMonitorsAsync()).Where(monitor => monitor.Id == secondId));
            Assert.Equal("b", unchanged.TabId);
            Assert.Equal(duplicateUrl, unchanged.Url);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GPTDeskTop.RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch { }
    }
}
''', encoding="utf-8")

Path("tests/GPTDeskTop.RuntimeTests/DuplicateOwnershipRepairUiRegressionTests.cs").write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class DuplicateOwnershipRepairUiRegressionTests
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
    public void RuntimeHealthOffersRepairForDuplicateOwnershipBlockers()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.Contains("(invalidMonitorCount > 0 || duplicateMonitorCount > 0)", source, StringComparison.Ordinal);
        Assert.Contains("Use Repair… to move a duplicate owner", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairDialogListsBothBlockerTypesAndOnlyUnownedStableTargets()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorIdentityRepairForm.cs");

        Assert.Contains("MonitorConversationOwnership.FindDuplicateMonitorIds(monitors)", source, StringComparison.Ordinal);
        Assert.Contains("duplicateIds.Contains(saved.Id)", source, StringComparison.Ordinal);
        Assert.Contains("!ownedConversationUrls.Contains(tab.Url)", source, StringComparison.Ordinal);
        Assert.Contains("new MonitorChoice(saved, duplicateIds.Contains(saved.Id))", source, StringComparison.Ordinal);
        Assert.Contains("_duplicateRepairService.RebindAsync", source, StringComparison.Ordinal);
        Assert.Contains("Duplicate owner", source, StringComparison.Ordinal);
        Assert.Contains("Invalid identity", source, StringComparison.Ordinal);
    }
}
''', encoding="utf-8")

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''## Release-Readiness Baseline\n''',
    '''- Legacy Duplicate Ownership Quarantine is merged on `main` (`7535c858ea95d192a34c738d46420e0adaf8ac79`): development delivery and crash recovery now use the shared `MonitorConversationOwnership` analyzer to quarantine every row in a legacy duplicate stable-conversation ownership group before opt-in, tab resolution, Chrome target selection or recovery delivery. Duplicate ownership records explicit diagnostics, cannot create success receipts, and keeps recovery pending without changing unique-owner behavior.\n- Operator Duplicate Ownership Runtime Boundary is merged on `main` (`229d0da9d5cc7b401fa7f8ecb045cabe93d6c293`): direct operator monitor start now refuses duplicate owners before worker creation, records `MonitorStartDuplicateConversationOwnership`, Runtime Health reports duplicate-owner counts as a degraded blocker, PendingRetry is disabled while duplicates remain, and privacy-safe Support Diagnostics exports only the aggregate duplicate-owner count with no monitor/conversation identity.\n\n## Release-Readiness Baseline\n''')

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''There is no open implementation issue after #53. Continue post-1.8 maintenance by auditing the next concrete operator/runtime gap, creating a tracked issue, implementing it on a branch, and merging only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''',
    '''Issue #61 is the current tracked post-1.8 task: provide a safe guided remediation path for legacy duplicate stable-conversation ownership by rebinding exactly one duplicate owner to an unowned stable conversation while preserving its Monitor ID, history, configuration, rotation state and recovery state. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''')

print("Issue #61 patch applied.")

from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected exactly one match, found {count}")
    file.write_text(text.replace(old, new), encoding="utf-8")


replace_once(
    "src/GPTDeskTop/Services/CrashRecoveryService.cs",
    '''                    firstTab = currentTabs.FirstOrDefault(t =>\n                        RuntimeHealthPresentation.IsChatGptConversationUrl(t.Url)\n                        && string.Equals(t.Url, firstValidMonitor.Url, StringComparison.OrdinalIgnoreCase));\n''',
    '''                    firstTab = currentTabs.FirstOrDefault(t =>\n                        ChatGptConversationIdentity.IsSame(t.Url, firstValidMonitor.Url));\n''')

replace_once(
    "src/GPTDeskTop/Services/CrashRecoveryService.cs",
    '''                if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))\n                {\n                    outcomes.Add(CrashRecoveryOutcome.SendFailed);\n                    await database.AddLogAsync(\n                        "System",\n                        "CrashRecovery",\n                        "Chrome did not return a stable ChatGPT conversation tab for the saved monitor URL.",\n                        "CrashRecoveryTabIdentityMismatch",\n                        saved.Id,\n                        tab.Id,\n                        saved.Title,\n                        cancellationToken);\n                    continue;\n                }\n\n                if (!string.IsNullOrWhiteSpace(tab.Id))\n                    usedTabIds.Add(tab.Id);\n\n                if (alreadyRecovered)\n''',
    '''                if (!ChatGptConversationIdentity.IsSame(tab.Url, saved.Url))\n                {\n                    outcomes.Add(CrashRecoveryOutcome.SendFailed);\n                    await database.AddLogAsync(\n                        "System",\n                        "CrashRecovery",\n                        "Chrome did not return the saved stable ChatGPT conversation identity for this monitor.",\n                        "CrashRecoveryTabIdentityMismatch",\n                        saved.Id,\n                        tab.Id,\n                        saved.Title,\n                        cancellationToken);\n                    continue;\n                }\n\n                var resolvedTitle = string.IsNullOrWhiteSpace(tab.Title) ? saved.Title : tab.Title;\n                var targetUpdated = await database.UpdateMonitorRuntimeTargetIfConversationMatchesAsync(\n                    saved.Id,\n                    saved.Url,\n                    tab.Id,\n                    resolvedTitle,\n                    cancellationToken);\n                if (!targetUpdated)\n                {\n                    outcomes.Add(CrashRecoveryOutcome.SendFailed);\n                    await database.AddLogAsync(\n                        "System",\n                        "CrashRecovery",\n                        "Saved monitor conversation identity changed while recovery was resolving its Chrome target. Recovery skipped this stale snapshot.",\n                        "CrashRecoverySavedConversationChanged",\n                        saved.Id,\n                        tab.Id,\n                        saved.Title,\n                        cancellationToken);\n                    continue;\n                }\n\n                saved.TabId = tab.Id;\n                saved.Title = resolvedTitle;\n                if (!string.IsNullOrWhiteSpace(tab.Id))\n                    usedTabIds.Add(tab.Id);\n\n                if (alreadyRecovered)\n''')

replace_once(
    "src/GPTDeskTop/Services/CrashRecoveryService.cs",
    '''                    outcomes.Add(CrashRecoveryOutcome.Success);\n                    saved.TabId = tab.Id;\n                    saved.Title = string.IsNullOrWhiteSpace(tab.Title) ? saved.Title : tab.Title;\n                    saved.Url = string.IsNullOrWhiteSpace(tab.Url) ? saved.Url : tab.Url;\n                    await database.SaveMonitorAsync(saved, cancellationToken);\n                    await database.AddLogAsync(\n''',
    '''                    outcomes.Add(CrashRecoveryOutcome.Success);\n                    await database.AddLogAsync(\n''')

replace_once(
    "src/GPTDeskTop/Services/CrashRecoveryService.cs",
    '''                saved.TabId = tab.Id;\n                saved.Title = string.IsNullOrWhiteSpace(tab.Title) ? saved.Title : tab.Title;\n                saved.Url = string.IsNullOrWhiteSpace(tab.Url) ? saved.Url : tab.Url;\n                await database.SaveMonitorAsync(saved, cancellationToken);\n\n                await database.AddLogAsync(\n''',
    '''                await database.AddLogAsync(\n''')

replace_once(
    "src/GPTDeskTop/Services/CrashRecoveryService.cs",
    '''            var exactTarget = tabs.FirstOrDefault(tab =>\n                !usedTabIds.Contains(tab.Id)\n                && string.Equals(tab.Id, monitor.TabId, StringComparison.Ordinal)\n                && RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url)\n                && string.Equals(tab.Url, monitor.Url, StringComparison.OrdinalIgnoreCase));\n''',
    '''            var exactTarget = tabs.FirstOrDefault(tab =>\n                !usedTabIds.Contains(tab.Id)\n                && string.Equals(tab.Id, monitor.TabId, StringComparison.Ordinal)\n                && ChatGptConversationIdentity.IsSame(tab.Url, monitor.Url));\n''')

replace_once(
    "src/GPTDeskTop/Services/CrashRecoveryService.cs",
    '''        return tabs.FirstOrDefault(tab =>\n            !usedTabIds.Contains(tab.Id)\n            && RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url)\n            && string.Equals(tab.Url, monitor.Url, StringComparison.OrdinalIgnoreCase));\n''',
    '''        return tabs.FirstOrDefault(tab =>\n            !usedTabIds.Contains(tab.Id)\n            && ChatGptConversationIdentity.IsSame(tab.Url, monitor.Url));\n''')

# Extend recovery identity tests with deterministic boundary coverage.
path = Path("tests/GPTDeskTop.RuntimeTests/CrashRecoveryConversationIdentityTests.cs")
text = path.read_text(encoding="utf-8")
insert_before = '''    private static async Task<SavedMonitor> SaveMonitorAsync(\n'''
new_tests = r'''    [Fact]
    public async Task PendingRetryRejectsReusedTargetIdAndFallsBackToSameConversationUrl()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var monitor = await SaveMonitorAsync(database, "target-reuse", "https://chatgpt.com/c/original", enabled: true);
            monitor.TabId = "reused-target";
            await database.SaveMonitorAsync(monitor);
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "target-reuse");

            var runtime = new FakeRuntime(
                [
                    Tab("reused-target", "https://chatgpt.com/c/different"),
                    Tab("correct-target", "https://chatgpt.com/c/original/")
                ],
                (_, _) => true);

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Single(runtime.Deliveries);
            Assert.Equal("correct-target", runtime.Deliveries[0].TabId);
            Assert.Single(runtime.StartedMonitors);
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
            Assert.Equal("correct-target", saved.TabId);
            Assert.Equal("https://chatgpt.com/c/original", saved.Url);
            Assert.Equal("0", await database.GetSettingAsync("CrashRecoveryPending"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task CreatedTabForDifferentConversationIsRejectedWithoutSendStartOrUrlMutation()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var monitor = await SaveMonitorAsync(database, "create-mismatch", "https://chatgpt.com/c/expected", enabled: true);
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "create-mismatch");

            var runtime = new FakeRuntime(
                [],
                (_, _) => true,
                createTab: _ => Tab("redirected", "https://chatgpt.com/c/unexpected"));

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Single(runtime.CreatedUrls);
            Assert.Empty(runtime.Deliveries);
            Assert.Empty(runtime.StartedMonitors);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
            Assert.Equal("https://chatgpt.com/c/expected", saved.Url);
            var logs = await database.GetRecentLogsForMonitorAsync(monitor.Id, 20);
            Assert.Contains(logs, log => log.Status == "CrashRecoveryTabIdentityMismatch");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ConcurrentSavedConversationChangeBeforeRecoverySendSkipsStaleSnapshot()
    {
        var root = CreateRoot();
        try
        {
            var database = new LocalDatabase(Path.Combine(root, "appdata.db"));
            await database.InitializeAsync();
            var monitor = await SaveMonitorAsync(database, "concurrent-change", "https://chatgpt.com/c/original", enabled: true);
            await database.SetSettingAsync("CrashRecoveryPending", "1");
            await database.SetSettingAsync("CrashRecovery.RecoveryId", "concurrent-change");

            var changed = false;
            var runtime = new FakeRuntime(
                [Tab("original-target", "https://chatgpt.com/c/original")],
                (_, _) => true,
                beforeGetTabs: async () =>
                {
                    if (changed) return;
                    changed = true;
                    var current = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
                    current.Url = "https://chatgpt.com/c/repaired";
                    current.TabId = "repair-target";
                    current.Title = "Repaired";
                    await database.SaveMonitorAsync(current);
                });

            await CrashRecoveryService.RecoverIfPendingAsync(
                runtime,
                database,
                CrashRecoveryMode.PendingRetry);

            Assert.Empty(runtime.Deliveries);
            Assert.Empty(runtime.StartedMonitors);
            Assert.Equal("1", await database.GetSettingAsync("CrashRecoveryPending"));
            var saved = Assert.Single(await database.GetSavedMonitorsAsync(), item => item.Id == monitor.Id);
            Assert.Equal("https://chatgpt.com/c/repaired", saved.Url);
            Assert.Equal("repair-target", saved.TabId);
            var logs = await database.GetRecentLogsForMonitorAsync(monitor.Id, 20);
            Assert.Contains(logs, log => log.Status == "CrashRecoverySavedConversationChanged");
        }
        finally
        {
            TryDelete(root);
        }
    }

'''
if text.count(insert_before) != 1:
    raise RuntimeError(f"expected recovery test insertion point once, found {text.count(insert_before)}")
text = text.replace(insert_before, new_tests + insert_before, 1)

text = text.replace(
'''        private readonly Func<ChromeTab, string, bool> _send;\n\n        public FakeRuntime(\n            IReadOnlyList<ChromeTab> initialTabs,\n            Func<ChromeTab, string, bool> send)\n        {\n            _initialTabs = initialTabs;\n            _send = send;\n        }\n''',
'''        private readonly Func<ChromeTab, string, bool> _send;\n        private readonly Func<string, ChromeTab>? _createTab;\n        private readonly Func<Task>? _beforeGetTabs;\n\n        public FakeRuntime(\n            IReadOnlyList<ChromeTab> initialTabs,\n            Func<ChromeTab, string, bool> send,\n            Func<string, ChromeTab>? createTab = null,\n            Func<Task>? beforeGetTabs = null)\n        {\n            _initialTabs = initialTabs;\n            _send = send;\n            _createTab = createTab;\n            _beforeGetTabs = beforeGetTabs;\n        }\n''')
text = text.replace(
'''        public Task<IReadOnlyList<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken)\n            => Task.FromResult(_initialTabs);\n\n        public Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken)\n        {\n            CreatedUrls.Add(url);\n            return Task.FromResult(Tab($"created-{CreatedUrls.Count}", url));\n        }\n''',
'''        public async Task<IReadOnlyList<ChromeTab>> GetTabsAsync(CancellationToken cancellationToken)\n        {\n            if (_beforeGetTabs is not null)\n                await _beforeGetTabs();\n            return _initialTabs;\n        }\n\n        public Task<ChromeTab> CreateTabAsync(string url, CancellationToken cancellationToken)\n        {\n            CreatedUrls.Add(url);\n            return Task.FromResult(_createTab?.Invoke(url) ?? Tab($"created-{CreatedUrls.Count}", url));\n        }\n''')
path.write_text(text, encoding="utf-8")

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''- Stable Conversation Target Revalidation is implemented in PR #66: persisted Chrome target IDs are treated only as runtime locators and are accepted only when the live target still represents the saved stable ChatGPT conversation. Reused/stale target IDs fall back to the exact normalized saved conversation URL, operator Start/Start All share the same safe resolver as development delivery, ordinary Start can update only TabId/Title/UpdatedAt when the persisted URL still matches its snapshot, and the runtime service rejects a monitor/tab conversation mismatch before worker creation.\n''',
    '''- Stable Conversation Target Revalidation is merged on `main` (`38465831a15db88d52c2f4b3ec9e250cdef8c187`): persisted Chrome target IDs are treated only as runtime locators and are accepted only when the live target still represents the saved stable ChatGPT conversation. Reused/stale target IDs fall back to the exact normalized saved conversation URL, operator Start/Start All share the same safe resolver as development delivery, ordinary Start can update only TabId/Title/UpdatedAt when the persisted URL still matches its snapshot, and the runtime service rejects a monitor/tab conversation mismatch before worker creation.\n''')

replace_once(
    "docs/DEVELOPMENT_STATUS.md",
    '''After stable-conversation target revalidation, continue post-1.8 maintenance by auditing the next concrete operator/runtime safety or supportability gap, create a tracked issue for the selected gap, implement it on an isolated branch, and merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''',
    '''Issue #67 is the current tracked post-1.8 task: apply the same stable-conversation identity invariant to crash recovery, reject redirected/reused recovery targets that do not match the saved conversation, and persist only guarded runtime target metadata before any recovery send/start. Merge only after the full CI gate set is green. Release publication remains a separate explicit operation and should not be performed implicitly.\n''')

print("Issue #67 patch applied.")

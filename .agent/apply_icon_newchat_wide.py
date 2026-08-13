from pathlib import Path
import base64

ROOT = Path.cwd()

def replace(path, old, new):
    p = ROOT / path
    text = p.read_text(encoding='utf-8')
    if old not in text:
        raise SystemExit(f'Expected patch anchor not found: {path}: {old[:100]!r}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')

# Materialize the user-provided GPTDeskTop icon as a real multi-size ICO committed to the repo.
b64_path = ROOT / 'src/GPTDeskTop/Assets/GPTDeskTop.ico.b64'
ico_path = ROOT / 'src/GPTDeskTop/Assets/GPTDeskTop.ico'
ico_path.parent.mkdir(parents=True, exist_ok=True)
ico_path.write_bytes(base64.b64decode(b64_path.read_text(encoding='utf-8').strip()))

# Brand the app executable/window and setup executable.
replace('src/GPTDeskTop/GPTDeskTop.csproj',
'''    <FileVersion>2.0.0.0</FileVersion>\n  </PropertyGroup>''',
'''    <FileVersion>2.0.0.0</FileVersion>\n    <ApplicationIcon>Assets\\GPTDeskTop.ico</ApplicationIcon>\n  </PropertyGroup>''')
replace('src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj',
'''    <FileVersion>2.0.0.0</FileVersion>\n    <PublishDir>''',
'''    <FileVersion>2.0.0.0</FileVersion>\n    <ApplicationIcon>..\\GPTDeskTop\\Assets\\GPTDeskTop.ico</ApplicationIcon>\n    <PublishDir>''')

# Make the Saved Monitors pane the dominant workspace (about 72% versus 28% open tabs),
# and hide Auto Reply from the Saved Monitor surface while preserving the setting internally.
replace('src/GPTDeskTop/UI/MainForm.cs',
'''        Text = $"GPTDeskTop v{GetAppVersion()}";\n        AutoScaleMode = AutoScaleMode.Dpi;''',
'''        Text = $"GPTDeskTop v{GetAppVersion()}";\n        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);\n        AutoScaleMode = AutoScaleMode.Dpi;''')
replace('src/GPTDeskTop/UI/MainForm.cs',
'''        editor.Controls.Add(FluentTheme.CreateMutedLabel("Auto reply"), 0, 1);\n        editor.Controls.Add(_autoReplyBox, 1, 1);\n        editor.Controls.Add(_enabledCheck, 2, 1);\n        editor.Controls.Add(_quickMonitorSettingsButton, 3, 1);''',
'''        editor.Controls.Add(_enabledCheck, 0, 1);\n        editor.SetColumnSpan(_enabledCheck, 3);\n        editor.Controls.Add(_quickMonitorSettingsButton, 3, 1);''')
replace('src/GPTDeskTop/UI/MainForm.cs',
'''        ApplySplitterMinimumsWhenFeasible(_workspaceSplit, 320, 420);''',
'''        ApplySplitterMinimumsWhenFeasible(_workspaceSplit, 240, 620);''')
replace('src/GPTDeskTop/UI/MainForm.cs',
'''        SetSplitRatio(_workspaceSplit, 0.42);''',
'''        SetSplitRatio(_workspaceSplit, 0.28);''')
replace('src/GPTDeskTop/UI/MainForm.cs',
'''            var workspaceRaw = await _database.GetSettingAsync("Ui.Main.WorkspaceSplitRatio");\n            if (!TryApplyStoredSplitRatio(_workspaceSplit, workspaceRaw))\n                SetSplitRatio(_workspaceSplit, 0.42);''',
'''            var workspaceRaw = await _database.GetSettingAsync("Ui.Main.WorkspaceSplitRatio");\n            if (!TryApplyStoredSplitRatio(_workspaceSplit, workspaceRaw) || GetSplitRatio(_workspaceSplit) > 0.34)\n                SetSplitRatio(_workspaceSplit, 0.28);''')
replace('src/GPTDeskTop/UI/MainForm.cs',
'''        _monitorsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SavedMonitor.AutoReply), HeaderText = "Auto reply", Width = 120 });\n''',
'''        // Auto Reply remains editable in Monitor Settings but is intentionally hidden from the main Saved Monitors grid.\n''')

# Use the branded executable icon for tray notifications too.
replace('src/GPTDeskTop/Services/TrayNotificationService.cs',
'''        _notifyIcon = new NotifyIcon { Icon = SystemIcons.Information, Text = "GPTDeskTop Chat Monitor", Visible = true, ContextMenuStrip = menu };''',
'''        var brandedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Information;\n        _notifyIcon = new NotifyIcon { Icon = brandedIcon, Text = "GPTDeskTop Chat Monitor", Visible = true, ContextMenuStrip = menu };''')

# Fix first-message verification across the navigation from / to /c/{conversation-id}.
replace('src/GPTDeskTop/Services/NewChatMonitorWorkflowService.cs',
'''            openedTab = await CreateFreshChatTabAsync(cancellationToken).ConfigureAwait(false);\n            var sent = await SendInitialMessageVerifiedAsync(openedTab, initialChatMessage, cancellationToken).ConfigureAwait(false);\n            if (!sent)\n                throw new InvalidOperationException("The initial ChatGPT message could not be verified after automatic Chrome/CDP recovery. No monitor was created; retry New Chat + Monitor.");\n\n            var stableTab = await ResolveStableConversationAsync(openedTab, cancellationToken).ConfigureAwait(false)\n                ?? throw new InvalidOperationException("The new ChatGPT target did not expose a stable conversation URL after verified delivery. No monitor was created.");''',
'''            openedTab = await CreateFreshChatTabAsync(cancellationToken).ConfigureAwait(false);\n            var sent = await SendInitialMessageVerifiedAsync(openedTab, initialChatMessage, cancellationToken).ConfigureAwait(false);\n\n            // The first send normally navigates ChatGPT from the new-chat shell to /c/{conversation-id}.\n            // A CDP target can therefore be valid enough to accept the click but stale when the receipt\n            // is read. Resolve the stable conversation and verify the SAME user message there before\n            // declaring failure. requireNewTurn:false prevents a duplicate send when the message already landed.\n            var stableTab = await ResolveStableConversationAsync(openedTab, cancellationToken).ConfigureAwait(false);\n            if (!sent && stableTab is not null)\n            {\n                sent = await _chrome.SendChatMessageVerifiedAsync(\n                    stableTab,\n                    initialChatMessage,\n                    cancellationToken,\n                    requireNewTurn: false).ConfigureAwait(false);\n            }\n\n            if (!sent)\n                throw new InvalidOperationException("The initial ChatGPT message could not be verified after stable-conversation recovery. The new tab was kept open for inspection and no duplicate monitor was created.");\n\n            stableTab ??= await ResolveStableConversationAsync(openedTab, cancellationToken).ConfigureAwait(false);\n            stableTab ??= throw new InvalidOperationException("The new ChatGPT target did not expose a stable conversation URL after verified delivery. No monitor was created.");''')

replace('src/GPTDeskTop/Services/ChromeDevToolsService.cs',
'''        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))\n        {\n            // A timed-out CDP command marks its session broken. Explicit invalidation guarantees the\n            // next verification uses a fresh session without treating the transient as a crash log.\n            _sessionPool.Invalidate(tab.Id);\n            return (false, 0, string.Empty);\n        }\n    }\n    private async Task<(int Count, string LastText)> GetUserMessageSnapshotAsync''',
'''        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))\n        {\n            // A timed-out CDP command marks its session broken. After the first ChatGPT message the\n            // target may also have navigated to /c/{id}; refresh the target metadata/WebSocket before\n            // the next verification so we do not keep reconnecting to the stale debugger URL.\n            _sessionPool.Invalidate(tab.Id);\n            await TryRefreshTabBindingAsync(tab, cancellationToken).ConfigureAwait(false);\n            return (false, 0, string.Empty);\n        }\n    }\n    private async Task TryRefreshTabBindingAsync(ChromeTab tab, CancellationToken cancellationToken)\n    {\n        try\n        {\n            var tabs = await GetTabsAsync(cancellationToken).ConfigureAwait(false);\n            var current = tabs.FirstOrDefault(candidate => string.Equals(candidate.Id, tab.Id, StringComparison.Ordinal));\n            if (current is not null)\n                RebindTab(tab, current);\n        }\n        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)\n        {\n            throw;\n        }\n        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))\n        {\n            // The next verification loop retries target discovery; no duplicate send is issued first.\n        }\n    }\n    private async Task<(int Count, string LastText)> GetUserMessageSnapshotAsync''')

# Project plan/status note.
planner = ROOT / 'TaskPlanner.md'
text = planner.read_text(encoding='utf-8')
marker = '| UI-021 | Compact the main dashboard hero into a 56px single-line status strip, preserve all four live metrics and keep the purpose text in accessibility/tooltips so Live Activity receives the reclaimed fixed space | UI / QA | High | Done | `CompactDashboardHeaderLayout.cs`, compact header regression tests, `docs/work/UI-021.md` |'
addition = marker + '\n| UI-022 | Brand GPTDeskTop with the supplied icon, make Saved Monitors the dominant 72% workspace, hide Auto Reply from the dashboard surface, and harden New Chat + Monitor receipt verification across first-message navigation | UI / Browser / QA | High | Done | `MainForm.cs`, `NewChatMonitorWorkflowService.cs`, `ChromeDevToolsService.cs`, app/setup icon, regression tests |'
if marker not in text:
    raise SystemExit('TaskPlanner UI-021 anchor not found')
planner.write_text(text.replace(marker, addition, 1), encoding='utf-8')

status = ROOT / 'docs/DEVELOPMENT_STATUS.md'
with status.open('a', encoding='utf-8') as f:
    f.write('\n\n## UI-022 — Branded wide Saved Monitors + first-message CDP rebind\n- Status: Done / CI pending.\n- Uses the supplied GPTDeskTop artwork as the application/setup icon and tray/window icon.\n- Saved Monitors defaults to about 72% of the primary workspace (roughly 2.6x the Open Conversations pane) and Auto Reply is hidden from the dashboard grid/card while remaining editable in monitor settings.\n- New Chat + Monitor now rebinds refreshed CDP target metadata after navigation and verifies the already-sent bootstrap message on the stable `/c/{id}` conversation before failing; no duplicate bootstrap send is created.\n')

# Regression coverage.
test = ROOT / 'tests/GPTDeskTop.RuntimeTests/BrandingNewChatWideMonitorRegressionTests.cs'
test.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class BrandingNewChatWideMonitorRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void SavedMonitorsAreDominantAndAutoReplyIsHiddenFromDashboard()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        Assert.Contains("ApplySplitterMinimumsWhenFeasible(_workspaceSplit, 240, 620)", source, StringComparison.Ordinal);
        Assert.Contains("SetSplitRatio(_workspaceSplit, 0.28)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderText = \"Auto reply\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FluentTheme.CreateMutedLabel(\"Auto reply\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstMessageVerificationRebindsAfterConversationNavigationWithoutDuplicateSend()
    {
        var workflow = ReadSource("src", "GPTDeskTop", "Services", "NewChatMonitorWorkflowService.cs");
        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("if (!sent && stableTab is not null)", workflow, StringComparison.Ordinal);
        Assert.Contains("requireNewTurn: false", workflow, StringComparison.Ordinal);
        Assert.Contains("TryRefreshTabBindingAsync", chrome, StringComparison.Ordinal);
        Assert.Contains("RebindTab(tab, current)", chrome, StringComparison.Ordinal);
    }

    [Fact]
    public void SuppliedBrandIconIsUsedByAppSetupWindowAndTray()
    {
        var app = ReadSource("src", "GPTDeskTop", "GPTDeskTop.csproj");
        var setup = ReadSource("src", "GPTDeskTop.Setup", "GPTDeskTop.Setup.csproj");
        var main = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var tray = ReadSource("src", "GPTDeskTop", "Services", "TrayNotificationService.cs");
        Assert.Contains("<ApplicationIcon>Assets\\GPTDeskTop.ico</ApplicationIcon>", app, StringComparison.Ordinal);
        Assert.Contains("GPTDeskTop.ico</ApplicationIcon>", setup, StringComparison.Ordinal);
        Assert.Contains("ExtractAssociatedIcon(Application.ExecutablePath)", main, StringComparison.Ordinal);
        Assert.Contains("ExtractAssociatedIcon(Application.ExecutablePath)", tray, StringComparison.Ordinal);
    }
}
''', encoding='utf-8')

print('UI-022 patch applied')

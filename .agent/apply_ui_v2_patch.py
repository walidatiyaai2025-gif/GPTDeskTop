from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(rel):
    return (ROOT / rel).read_bytes().decode("utf-8")


def write(rel, text):
    (ROOT / rel).write_bytes(text.encode("utf-8"))


def line_ending(text):
    return "\r\n" if "\r\n" in text else "\n"


def adapt(text, nl):
    return text.replace("\n", nl)


def replace_once(rel, old, new):
    text = read(rel)
    nl = line_ending(text)
    old = adapt(old, nl)
    new = adapt(new, nl)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{rel}: expected one exact replacement, found {count}")
    write(rel, text.replace(old, new, 1))


def regex_once(rel, pattern, replacement):
    text = read(rel)
    nl = line_ending(text)
    pattern = pattern.replace("\\n", "\\r?\\n")
    matches = list(re.finditer(pattern, text, flags=re.S))
    if len(matches) != 1:
        raise RuntimeError(f"{rel}: expected one regex replacement, found {len(matches)}")
    replacement = adapt(replacement, nl)
    write(rel, text[:matches[0].start()] + replacement + text[matches[0].end():])


def append_once(rel, marker, block):
    text = read(rel)
    if marker in text:
        return
    nl = line_ending(text)
    suffix = "" if text.endswith(("\n", "\r")) else nl
    write(rel, text + suffix + adapt(block, nl))


# 1) Commands menu: Live activity/history becomes an on-demand window.
replace_once(
    "src/GPTDeskTop/UI/CompactTopCommandMenuExperience.cs",
    '''        var focusLog = new ToolStripMenuItem("Focus Live Activity")
        {
            ToolTipText = "Move keyboard focus to the live activity/log output."
        };
        focusLog.Click += (_, _) => FocusLiveActivity(form);
        root.DropDownItems.Add(focusLog);
''',
    '''        var focusLog = new ToolStripMenuItem("Live Monitor & History")
        {
            ToolTipText = "Open live activity and stored history only when you need them."
        };
        focusLog.Click += (_, _) => OperatorWorkspaceV2Experience.ShowLiveMonitor((MainForm)form);
        root.DropDownItems.Add(focusLog);
''')

# Keep the established menu regression aligned with the operator-first layout.
replace_once(
    "tests/GPTDeskTop.RuntimeTests/CompactTopCommandMenuUiRegressionTests.cs",
    '        Assert.Contains("Focus Live Activity", source, StringComparison.Ordinal);\n',
    '        Assert.Contains("Live Monitor & History", source, StringComparison.Ordinal);\n')

# 2) Development header stays alive as a command source but exposes a tiny footer summary.
replace_once(
    "src/GPTDeskTop/UI/DevelopmentTaskDashboardControl.cs",
    '''    public event EventHandler? ExpandedChanged;

    public bool IsExpanded
''',
    '''    public event EventHandler? ExpandedChanged;

    public string FooterSummary
    {
        get
        {
            var status = _status.Text.Trim().TrimStart('●').Trim();
            var phase = _phase.Text.Trim();
            var countdown = _countdown.Text.Trim();
            return string.Join(" • ", new[] { status, phase, countdown }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal));
        }
    }

    public bool IsExpanded
''')

# 3) Settings: separate configurable continuation for generic ChatGPT-rendered errors.
replace_once(
    "src/GPTDeskTop/UI/SettingsForm.cs",
    '    private readonly TextBox _timeoutRecovery = new() { Dock = DockStyle.Fill, Text = "كمل" };\n',
    '    private readonly TextBox _chatGptErrorRecovery = new() { Dock = DockStyle.Fill, Text = "كمل من آخر نقطة مؤكدة واستمر بدون تكرار ما تم إنجازه." };\n    private readonly TextBox _timeoutRecovery = new() { Dock = DockStyle.Fill, Text = "كمل" };\n')

replace_once(
    "src/GPTDeskTop/UI/SettingsForm.cs",
    '''        var layout = CreateSettingsLayout(9);
        AddSectionTitle(layout, 0, "Conversation continuity", "Proactively rotate chats before they become too long while preserving the same Monitor ID.");
        AddRow(layout, 2, "Rotate after assistant messages (0 = off)", _rotateAfterMessages, "0 disables proactive message-count rotation. The current visible assistant count is used.");
        AddRow(layout, 3, "Message-count new Chat start message", _messageCountRotationStartMessage, "Fixed message sent after a successful message-count rotation.");
        AddSectionTitle(layout, 5, "Timeout recovery", "Used when ChatGPT reports a message-delivery timeout and a recovery chat is created.");
        AddRow(layout, 7, "Recovery message", _timeoutRecovery, "Message sent to the newly-created recovery conversation.");
''',
    '''        var layout = CreateSettingsLayout(10);
        AddSectionTitle(layout, 0, "Conversation continuity", "Proactively rotate chats before they become too long while preserving the same Monitor ID.");
        AddRow(layout, 2, "Rotate after assistant messages (0 = off)", _rotateAfterMessages, "0 disables proactive message-count rotation. The current visible assistant count is used.");
        AddRow(layout, 3, "Message-count new Chat start message", _messageCountRotationStartMessage, "Fixed message sent after a successful message-count rotation.");
        AddSectionTitle(layout, 5, "Automatic error recovery", "ChatGPT-rendered errors create a fresh conversation under the same Monitor ID; delivery timeouts keep their own recovery template.");
        AddRow(layout, 7, "ChatGPT error continuation", _chatGptErrorRecovery, "Message sent after a ChatGPT page error forces a fresh recovery chat.");
        AddRow(layout, 8, "Delivery-timeout recovery", _timeoutRecovery, "Message sent when ChatGPT explicitly reports a message-delivery timeout.");
''')

replace_once(
    "src/GPTDeskTop/UI/SettingsForm.cs",
    '''        ConfigureAccessible(_messageCountRotationStartMessage, "Rotation start message", "Message sent in the new conversation after message-count rotation.", 1);
        ConfigureAccessible(_timeoutRecovery, "Timeout recovery message", "Message sent to a recovery conversation after a delivery timeout.", 2);
''',
    '''        ConfigureAccessible(_messageCountRotationStartMessage, "Rotation start message", "Message sent in the new conversation after message-count rotation.", 1);
        ConfigureAccessible(_chatGptErrorRecovery, "ChatGPT error continuation message", "Message sent in a fresh conversation after ChatGPT renders an explicit error.", 2);
        ConfigureAccessible(_timeoutRecovery, "Timeout recovery message", "Message sent to a recovery conversation after a delivery timeout.", 3);
''')

replace_once(
    "src/GPTDeskTop/UI/SettingsForm.cs",
    '''            _messageCountRotationStartMessage.Text = await _database.GetSettingAsync("MessageCountRotationStartMessage") ?? "كمل";
            _timeoutRecovery.Text = await _database.GetSettingAsync("TimeoutRecoveryMessage") ?? "كمل";
''',
    '''            _messageCountRotationStartMessage.Text = await _database.GetSettingAsync("MessageCountRotationStartMessage") ?? "كمل";
            _chatGptErrorRecovery.Text = await _database.GetSettingAsync("ChatGptErrorContinuationMessage") ?? "كمل من آخر نقطة مؤكدة واستمر بدون تكرار ما تم إنجازه.";
            _timeoutRecovery.Text = await _database.GetSettingAsync("TimeoutRecoveryMessage") ?? "كمل";
''')

replace_once(
    "src/GPTDeskTop/UI/SettingsForm.cs",
    '''            ["MessageCountRotationStartMessage"] = rotationStartMessage,
            ["TimeoutRecoveryMessage"] = string.IsNullOrWhiteSpace(_timeoutRecovery.Text) ? "كمل" : _timeoutRecovery.Text.Trim(),
''',
    '''            ["MessageCountRotationStartMessage"] = rotationStartMessage,
            ["ChatGptErrorContinuationMessage"] = string.IsNullOrWhiteSpace(_chatGptErrorRecovery.Text) ? "كمل من آخر نقطة مؤكدة واستمر بدون تكرار ما تم إنجازه." : _chatGptErrorRecovery.Text.Trim(),
            ["TimeoutRecoveryMessage"] = string.IsNullOrWhiteSpace(_timeoutRecovery.Text) ? "كمل" : _timeoutRecovery.Text.Trim(),
''')

replace_once(
    "src/GPTDeskTop/Services/ConfigurationBackupService.cs",
    '''        "MessageCountRotationStartMessage",
        "NoResponseRefreshSeconds",
        "TimeoutRecoveryMessage",
''',
    '''        "MessageCountRotationStartMessage",
        "NoResponseRefreshSeconds",
        "ChatGptErrorContinuationMessage",
        "TimeoutRecoveryMessage",
''')

# 4) Generic ChatGPT error UI gets a verified fresh-chat handoff under the same monitor ID.
regex_once(
    "src/GPTDeskTop/Services/ChatGptMonitorService.cs",
    r'''                    if \(isError\)\n                    \{ Activity\?\.Invoke\(monitor\.Id, \$"\{prefix\} Error saved\. Refreshing only this tab\.\.\."\);.*?continue; \}\n                    if \(replyDelaySeconds > 0\)''',
    '''                    if (isError)
                    {
                        var recoveryMessage = await _database.GetSettingAsync("ChatGptErrorContinuationMessage", cancellationToken)
                            ?? "كمل من آخر نقطة مؤكدة واستمر بدون تكرار ما تم إنجازه.";
                        Activity?.Invoke(monitor.Id, $"{prefix} ChatGPT error saved. Opening a fresh chat and continuing under the same Monitor ID...");
                        var oldTab = tab;
                        var newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);
                        await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken);
                        await ApplyModelRouteAsync(monitor, newTab, recovery: true, contextRotation: false, cancellationToken);
                        await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken);
                        var sent = await SendWhenReadyAsync(monitor.Id, newTab, recoveryMessage, allowRecoveryReload: true, cancellationToken);
                        if (!sent)
                        {
                            Activity?.Invoke(monitor.Id, $"{prefix} ChatGPT-error continuation was not verified. Closing the unused recovery chat and retrying later.");
                            await _database.AddLogAsync("System", recoveryMessage, text, "ChatGptErrorRecoverySendDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken);
                            HistoryChanged?.Invoke();
                            try { await _chrome.CloseTabAsync(newTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Deferred ChatGPT-error recovery tab close failed transiently: {closeEx.Message}"); }
                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                            continue;
                        }

                        var committedRecoveryTab = await CommitVerifiedConversationHandoffAsync(
                            monitor, oldTab, newTab, recoveryMessage, text,
                            rotationTrigger: "ChatGptError",
                            successStatus: "RecoveredFromChatGptError",
                            outboundStatus: "ChatGptErrorContinuationSent",
                            conflictStatus: "ChatGptErrorHandoffCommitDeferred",
                            incrementRotationCount: false,
                            recordRotation: false,
                            cancellationToken);
                        if (committedRecoveryTab is null)
                        {
                            lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                            continue;
                        }

                        tab = committedRecoveryTab;
                        lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;
                        Activity?.Invoke(monitor.Id, $"{prefix} ChatGPT error recovery complete. New conversation is monitored as Monitor #{monitor.Id}.");
                        try { await _chrome.CloseTabAsync(oldTab, cancellationToken); } catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Old errored chat close was deferred: {closeEx.Message}"); }
                        continue;
                    }
                    if (replyDelaySeconds > 0)''')

# 5) Persistent connection/CDP recovery: refresh current conversation first, wait, then reopen
# the same conversation in a fresh tab before browser-level restart is considered.
regex_once(
    "src/GPTDeskTop/Services/ChromeDevToolsService.cs",
    r'''    private async Task<bool> RecoverMonitorTabAsync\(ChromeTab tab, CancellationToken cancellationToken\)\n    \{.*?\n    \}\n    private async Task<List<ChromeTab>\?> TryGetLiveTabsAsync''',
    '''    private async Task<bool> RecoverMonitorTabAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            return false;

        await _monitorBrowserRecoveryGate.WaitAsync(cancellationToken);
        try
        {
            var replacement = await TryFindConversationTabAsync(tab.Url, cancellationToken);
            if (replacement is not null)
            {
                // First escalation level: keep the exact conversation/target, force a clean CDP
                // session, reload it, and give ChatGPT a bounded window to restore real content.
                if (await RefreshConversationTabAsync(replacement, cancellationToken))
                {
                    RebindTab(tab, replacement);
                    return true;
                }

                // Second escalation level: the refreshed tab never became readable. Open the exact
                // saved /c/{conversation-id} URL in a new target; close the stale target only after
                // the replacement proves it can expose conversation state.
                replacement = await ReopenConversationTabAsync(tab.Url, replacement, cancellationToken);
                if (replacement is not null)
                {
                    RebindTab(tab, replacement);
                    return true;
                }
            }

            var liveTabs = await TryGetLiveTabsAsync(cancellationToken);
            if (liveTabs is { Count: > 0 })
            {
                replacement = await ReopenConversationTabAsync(tab.Url, tab, cancellationToken);
                if (replacement is not null)
                {
                    RebindTab(tab, replacement);
                    return true;
                }
            }

            // Do not kill a healthy hidden browser because of a short DevTools transport blip.
            // Require the endpoint to remain unavailable across a bounded grace window first.
            liveTabs = await WaitForLiveTabsAfterTransportFailureAsync(cancellationToken);
            if (liveTabs is { Count: > 0 })
            {
                replacement = liveTabs.FirstOrDefault(candidate =>
                    RuntimeHealthPresentation.IsChatGptConversationUrl(candidate.Url)
                    && ChatGptConversationIdentity.IsSame(tab.Url, candidate.Url));

                if (replacement is not null && await RefreshConversationTabAsync(replacement, cancellationToken))
                {
                    RebindTab(tab, replacement);
                    return true;
                }

                replacement = await ReopenConversationTabAsync(tab.Url, replacement ?? tab, cancellationToken);
                if (replacement is not null)
                {
                    RebindTab(tab, replacement);
                    return true;
                }
            }

            // The endpoint stayed unavailable through the grace window. Now a real browser restart
            // is justified. Preserve the operator's hidden preference across that recovery.
            var restoreHidden = _monitorChromeHidden;
            await CloseAllMonitorTabsAsync(cancellationToken);
            LaunchMonitorChrome(tab.Url);

            replacement = await WaitForConversationTabAsync(tab.Url, cancellationToken);
            if (replacement is null)
            {
                liveTabs = await TryGetLiveTabsAsync(cancellationToken);
                if (liveTabs is not { Count: > 0 })
                    return false;
                replacement = await CreateTabAsync(tab.Url, cancellationToken);
            }

            if (!await WaitForReadableConversationStateAsync(replacement, cancellationToken))
                return false;

            RebindTab(tab, replacement);
            if (restoreHidden)
                await HideMonitorChromeAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ExceptionLogService.Log(ex, "ChromeDevToolsService.AutoRecoverMonitorTab", null, tab.Id, tab.Title);
            return false;
        }
        finally
        {
            _monitorBrowserRecoveryGate.Release();
        }
    }

    private async Task<bool> RefreshConversationTabAsync(ChromeTab conversationTab, CancellationToken cancellationToken)
    {
        try
        {
            _sessionPool.Invalidate(conversationTab.Id);
            await ReloadTabAsync(conversationTab, cancellationToken);
            return await WaitForReadableConversationStateAsync(conversationTab, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))
        {
            return false;
        }
    }

    private async Task<ChromeTab?> ReopenConversationTabAsync(string conversationUrl, ChromeTab? staleTab, CancellationToken cancellationToken)
    {
        ChromeTab? reopened = null;
        try
        {
            reopened = await CreateTabAsync(conversationUrl, cancellationToken);
            if (!await WaitForReadableConversationStateAsync(reopened, cancellationToken))
            {
                await CloseTabAsync(reopened, cancellationToken);
                return null;
            }

            if (staleTab is not null && !string.Equals(staleTab.Id, reopened.Id, StringComparison.Ordinal))
                await CloseTabAsync(staleTab, cancellationToken);

            return reopened;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (reopened is not null) await CloseTabAsync(reopened, CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))
        {
            if (reopened is not null) await CloseTabAsync(reopened, CancellationToken.None);
            return null;
        }
    }

    private async Task<bool> WaitForReadableConversationStateAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var state = await ReadChatStateCoreAsync(tab, cancellationToken);
                if (state.AssistantCount > 0
                    || state.IsGenerating
                    || !string.IsNullOrWhiteSpace(state.LastAssistantText)
                    || !string.IsNullOrWhiteSpace(state.ErrorText))
                    return true;
            }
            catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))
            {
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    private async Task<List<ChromeTab>?> TryGetLiveTabsAsync''')

# 6) Final release identity 2.0.0 across app/setup/build packaging metadata.
replace_once(
    "src/GPTDeskTop/GPTDeskTop.csproj",
    '''    <Version>1.8.0</Version>
    <AssemblyVersion>1.8.0.0</AssemblyVersion>
    <FileVersion>1.8.0.0</FileVersion>
''',
    '''    <Version>2.0.0</Version>
    <AssemblyVersion>2.0.0.0</AssemblyVersion>
    <FileVersion>2.0.0.0</FileVersion>
''')

setup = read("src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj")
if setup.count("1.8.0") < 4:
    raise RuntimeError("Setup version anchors changed unexpectedly")
write("src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj", setup.replace("1.8.0", "2.0.0"))

build = read("src/GPTDeskTop.Build/GPTDeskTop.Build.csproj")
if build.count("GPTDeskTop v1.8.0") != 2:
    raise RuntimeError("Build release metadata anchors changed unexpectedly")
write("src/GPTDeskTop.Build/GPTDeskTop.Build.csproj", build.replace("GPTDeskTop v1.8.0", "GPTDeskTop v2.0.0"))

# 7) Team plan/status: append only, never rewrite another agent's current entries.
append_once(
    "TaskPlanner.md",
    "UI-V2-001",
    '''

## UI-V2-001 — Dashboard-first operator workspace + resilient recovery (2026-08-12)
- **Status:** IMPLEMENTED ON BRANCH — pending PR verification/merge.
- Main page prioritizes Open ChatGPT Conversations + Saved/Running Monitors.
- Live Monitor & Stored History moved to an on-demand Commands window.
- Development Plan header removed from permanent layout; compact development state is centered in the version footer and controls/messages remain in Commands.
- Persistent connection recovery escalates: retry -> reload same conversation -> wait -> reopen same conversation target -> browser restart only after endpoint grace.
- Explicit ChatGPT error UI creates a verified fresh chat, sends the configurable `ChatGptErrorContinuationMessage`, atomically rebinds the same Monitor ID, then closes the errored old tab.
- Final release identity target: **2.0.0**.
''')

append_once(
    "docs/DEVELOPMENT_STATUS.md",
    "UI-V2-001",
    '''

### 2026-08-12 — UI-V2-001 / MON-015 / REL-003
- **UI-V2-001:** operator-first main workspace; Live Monitor/History and sent-message catalog are on demand from Commands; Development Plan header removed and useful state moved into the footer.
- **MON-015:** transport recovery now refreshes the same conversation first, waits for readable state, then reopens the exact conversation URL before browser restart. Explicit ChatGPT error UI performs a transactional fresh-chat continuation under the same Monitor ID.
- **REL-003:** application, setup and packaging metadata advanced to **2.0.0** after these changes.
- Verification requirement before merge: full RuntimeTests + Release build/packaging gates green.
''')

print("UI v2 patch applied successfully")

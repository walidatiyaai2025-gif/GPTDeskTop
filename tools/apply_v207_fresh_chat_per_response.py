from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MONITOR = ROOT / 'src/GPTDeskTop/Services/ChatGptMonitorService.cs'


def replace_once(path: Path, old: str, new: str):
    text = path.read_text(encoding='utf-8')
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'Expected one match in {path}, found {count}: {old[:120]!r}')
    path.write_text(text.replace(old, new, 1), encoding='utf-8')


# Every completed assistant response now hands off to a brand-new conversation.
anchor = '''                    ResponseReceived?.Invoke(monitor.Id, monitor.Title, text, isError);\n\n                    if (messageCountThresholdReached && !rotationSlotAvailable)\n'''
replacement = '''                    ResponseReceived?.Invoke(monitor.Id, monitor.Title, text, isError);\n\n                    if (!isError)\n                    {\n                        var freshTab = await ContinueInFreshChatAfterResponseAsync(\n                            monitor, tab, text, replyDelaySeconds, cancellationToken);\n                        if (freshTab is not null)\n                        {\n                            tab = freshTab;\n                            lastHandledText = string.Empty;\n                        }\n                        else\n                        {\n                            // The source response remains handled. We never send another automated\n                            // message into the old conversation; a later poll will reconcile any\n                            // accepted handoff checkpoint or retry creation of a fresh chat.\n                            lastHandledText = text;\n                        }\n                        candidateText = string.Empty;\n                        candidateSince = DateTimeOffset.MinValue;\n                        continue;\n                    }\n\n                    if (messageCountThresholdReached && !rotationSlotAvailable)\n'''
replace_once(MONITOR, anchor, replacement)

# Remove the legacy same-conversation auto-reply path entirely.
legacy = '''                    if (replyDelaySeconds > 0)\n                    {\n                        Activity?.Invoke(monitor.Id, $"{prefix} Waiting {replyDelaySeconds}s before auto reply...");\n                        await Task.Delay(TimeSpan.FromSeconds(replyDelaySeconds), cancellationToken);\n                        var recheck = await _chrome.GetChatStateAsync(tab, cancellationToken);\n                        var latestText = GetEffectiveResponse(recheck);\n                        if (recheck.IsGenerating || !string.Equals(latestText, text, StringComparison.Ordinal))\n                        {\n                            await _database.AddLogAsync("System", monitor.AutoReply, latestText, "SendDelayCancelled", monitor.Id, tab.Id, monitor.Title, cancellationToken);\n                            HistoryChanged?.Invoke();\n                            continue;\n                        }\n                    }\n\n                    var autoSent = await SendWhenReadyAsync(monitor.Id, tab, monitor.AutoReply, allowRecoveryReload: false, cancellationToken);\n                    await _database.AddLogAsync("Outbound", monitor.AutoReply, string.Empty, autoSent ? "Sent" : "Failed", monitor.Id, tab.Id, monitor.Title, cancellationToken);\n                    HistoryChanged?.Invoke();\n                    await Task.Delay(Math.Max(250, _config.DelayAfterSendMilliseconds), cancellationToken);\n'''
replace_once(MONITOR, legacy, '''                    // Normal successful responses are handled above by the mandatory fresh-chat handoff.\n                    continue;\n''')

marker = '''    private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync(\n'''
helper = '''    private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync(\n        SavedMonitor monitor,\n        ChromeTab oldTab,\n        string responseText,\n        int replyDelaySeconds,\n        CancellationToken cancellationToken)\n    {\n        var prefix = $"[{monitor.Title}]";\n        if (replyDelaySeconds > 0)\n        {\n            Activity?.Invoke(monitor.Id, $"{prefix} Waiting {replyDelaySeconds}s before fresh-chat follow-up...");\n            await Task.Delay(TimeSpan.FromSeconds(replyDelaySeconds), cancellationToken);\n            var recheck = await GetChatStateWithRetryAsync(monitor.Id, oldTab, cancellationToken);\n            var latestText = GetEffectiveResponse(recheck);\n            if (recheck.IsGenerating || !string.Equals(latestText, responseText, StringComparison.Ordinal))\n            {\n                await _database.AddLogAsync("System", monitor.AutoReply, latestText, "FreshChatSendDelayCancelled", monitor.Id, oldTab.Id, monitor.Title, cancellationToken);\n                HistoryChanged?.Invoke();\n                return null;\n            }\n        }\n\n        if (_globalRateLimit.IsActive)\n        {\n            Activity?.Invoke(monitor.Id, $"{prefix} Fresh-chat follow-up deferred by global ChatGPT rate limit.");\n            return null;\n        }\n        if (!await CanPerformDestructiveAutomationAsync(monitor.Id, oldTab, "fresh-chat-per-response", cancellationToken))\n            return null;\n\n        var startMessage = monitor.AutoReply.Trim();\n        Activity?.Invoke(monitor.Id, $"{prefix} Response complete. Opening a brand-new chat; no further automated message will be sent to the old conversation.");\n        RuntimeFlightRecorder.Record("Monitor", "FreshChatPerResponse", "started", "new-chat-for-every-response", monitor.Id, oldTab.Id, oldTab.Url);\n\n        await ConversationHandoffCheckpointStore.PrepareAsync(\n            _database, monitor, oldTab, "EveryResponseFreshChat", startMessage, responseText,\n            "FreshChatPerResponseCommitted", "FreshChatFollowUpSent", "FreshChatPerResponseCommitDeferred",\n            incrementRotationCount: false, recordRotation: false, cancellationToken);\n        await _database.AddLogAsync("System", startMessage, responseText, "FreshChatHandoffPrepared", monitor.Id, oldTab.Id, monitor.Title, cancellationToken);\n        HistoryChanged?.Invoke();\n\n        ChromeTab newTab;\n        try\n        {\n            newTab = await _chrome.CreateNewChatTabAsync(cancellationToken);\n            await ConversationHandoffCheckpointStore.MarkTargetCreatedAsync(_database, monitor.Id, newTab, cancellationToken);\n            await WaitForChatReadyAsync(monitor.Id, newTab, cancellationToken);\n            await ApplyModelRouteAsync(monitor, newTab, recovery: false, contextRotation: true, cancellationToken);\n            await Task.Delay(Math.Max(500, _config.DelayAfterSendMilliseconds), cancellationToken);\n        }\n        catch (Exception ex) when (IsTransientChromeException(ex))\n        {\n            Activity?.Invoke(monitor.Id, $"{prefix} Fresh-chat creation is waiting for Chrome/CDP recovery: {ex.GetType().Name}.");\n            return null;\n        }\n\n        bool sent;\n        using (RuntimeFlightRecorder.BeginScope(monitor.Id, newTab.Id, newTab.Url))\n            sent = await SendWhenReadyAsync(monitor.Id, newTab, startMessage, allowRecoveryReload: true, cancellationToken);\n        if (sent)\n            await ConversationHandoffCheckpointStore.MarkDeliveryAcceptedAsync(_database, monitor.Id, newTab, cancellationToken);\n\n        if (!sent)\n        {\n            Activity?.Invoke(monitor.Id, $"{prefix} Fresh-chat follow-up was not verified. The old conversation remains untouched; the unused new chat is closed.");\n            await _database.AddLogAsync("System", startMessage, responseText, "FreshChatFollowUpDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken);\n            HistoryChanged?.Invoke();\n            await ConversationHandoffCheckpointStore.ClearAsync(_database, monitor.Id, cancellationToken);\n            try { await _chrome.CloseTabAsync(newTab, cancellationToken); }\n            catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Unused fresh-chat close was deferred: {closeEx.Message}"); }\n            return null;\n        }\n\n        var committedTab = await CommitVerifiedConversationHandoffAsync(\n            monitor, oldTab, newTab, startMessage, responseText,\n            rotationTrigger: "EveryResponseFreshChat",\n            successStatus: "FreshChatPerResponseCommitted",\n            outboundStatus: "FreshChatFollowUpSent",\n            conflictStatus: "FreshChatPerResponseCommitDeferred",\n            incrementRotationCount: false,\n            recordRotation: false,\n            cancellationToken);\n        if (committedTab is null)\n        {\n            Activity?.Invoke(monitor.Id, $"{prefix} Fresh-chat delivery was accepted and checkpointed; binding reconciliation will finish without resending.");\n            return null;\n        }\n\n        _autonomousTasks.Rollover(monitor.Id, committedTab.Url);\n        try { await _chrome.CloseTabAsync(oldTab, cancellationToken); }\n        catch (Exception closeEx) when (IsTransientChromeException(closeEx)) { Activity?.Invoke(monitor.Id, $"Old completed chat close was deferred: {closeEx.Message}"); }\n        RuntimeFlightRecorder.Record("Monitor", "FreshChatPerResponse", "completed", "old-chat-closed-new-chat-bound", monitor.Id, committedTab.Id, committedTab.Url);\n        Activity?.Invoke(monitor.Id, $"{prefix} Fresh-chat continuation complete. Old chat closed; Monitor #{monitor.Id} is bound to the new conversation.");\n        return committedTab;\n    }\n\n'''
text = MONITOR.read_text(encoding='utf-8')
if helper not in text:
    if text.count(marker) != 1:
        raise RuntimeError('Could not locate handoff helper insertion marker')
    MONITOR.write_text(text.replace(marker, helper + marker, 1), encoding='utf-8')

# Release identity.
for rel in [
    'src/GPTDeskTop/GPTDeskTop.csproj',
    'src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj',
    'src/GPTDeskTop.Build/GPTDeskTop.Build.csproj',
    'tests/GPTDeskTop.RuntimeTests/OperatorWorkspaceV2RegressionTests.cs',
]:
    p = ROOT / rel
    if p.exists():
        data = p.read_text(encoding='utf-8')
        data = data.replace('2.0.6', '2.0.7')
        p.write_text(data, encoding='utf-8')

# Regression test: a normal response must never auto-send back into the source conversation.
test = ROOT / 'tests/GPTDeskTop.RuntimeTests/FreshChatPerResponseRegressionTests.cs'
test.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class FreshChatPerResponseRegressionTests
{
    [Fact]
    public void NormalResponseAlwaysMovesContinuationToBrandNewChat()
    {
        var source = ReadMonitorSource();
        var loop = Slice(source, "private async Task MonitorLoopAsync", "private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync");
        Assert.Contains("if (!isError)", loop, StringComparison.Ordinal);
        Assert.Contains("ContinueInFreshChatAfterResponseAsync(", loop, StringComparison.Ordinal);
        Assert.Contains("continue;", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("var autoSent = await SendWhenReadyAsync(monitor.Id, tab, monitor.AutoReply", loop, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshChatContinuationUsesExactConfiguredFollowUpAndClosesOldChatAfterVerifiedHandoff()
    {
        var source = ReadMonitorSource();
        var method = Slice(source, "private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync", "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");
        Assert.Contains("var startMessage = monitor.AutoReply.Trim();", method, StringComparison.Ordinal);
        Assert.Contains("CreateNewChatTabAsync", method, StringComparison.Ordinal);
        Assert.Contains("SendWhenReadyAsync(monitor.Id, newTab, startMessage", method, StringComparison.Ordinal);
        Assert.Contains("rotationTrigger: \"EveryResponseFreshChat\"", method, StringComparison.Ordinal);
        Assert.Contains("CommitVerifiedConversationHandoffAsync", method, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync(oldTab", method, StringComparison.Ordinal);
        Assert.Contains("Old chat closed", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SendWhenReadyAsync(monitor.Id, oldTab", method, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedFreshChatSendNeverFallsBackToOldConversation()
    {
        var source = ReadMonitorSource();
        var method = Slice(source, "private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync", "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");
        Assert.Contains("if (!sent)", method, StringComparison.Ordinal);
        Assert.Contains("old conversation remains untouched", method, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync(newTab", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SendWhenReadyAsync(monitor.Id, oldTab", method, StringComparison.Ordinal);
    }

    private static string ReadMonitorSource()
        => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }
}
''', encoding='utf-8')

print('v2.0.7 fresh-chat-per-response patch applied')

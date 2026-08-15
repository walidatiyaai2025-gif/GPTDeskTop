from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one match in {path}, found {count}: {old[:100]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


# 1) Suppress successful routine Runtime.evaluate command pairs while retaining failures.
session = "src/GPTDeskTop/Services/ChromeDevToolsSessionPool.cs"
replace_once(
    session,
    '''        RuntimeFlightRecorder.Record("CDP", "CommandRequested", "started", method, tabId: tab.Id, conversationRef: tab.Url);\n        var session = GetOrCreateSession(tab);\n        return SendInstrumentedAsync(session, tab, method, parameters, cancellationToken, extractRuntimeValue);''',
    '''        var recordCommandLifecycle = ShouldRecordCommandLifecycle(method);\n        if (recordCommandLifecycle)\n            RuntimeFlightRecorder.Record("CDP", "CommandRequested", "started", method, tabId: tab.Id, conversationRef: tab.Url);\n        var session = GetOrCreateSession(tab);\n        return SendInstrumentedAsync(session, tab, method, parameters, cancellationToken, extractRuntimeValue, recordCommandLifecycle);''')
replace_once(
    session,
    '''    private static async Task<JsonElement> SendInstrumentedAsync(\n        DevToolsSession session,\n        ChromeTab tab,\n        string method,\n        object parameters,\n        CancellationToken cancellationToken,\n        bool extractRuntimeValue)''',
    '''    private static async Task<JsonElement> SendInstrumentedAsync(\n        DevToolsSession session,\n        ChromeTab tab,\n        string method,\n        object parameters,\n        CancellationToken cancellationToken,\n        bool extractRuntimeValue,\n        bool recordCommandLifecycle)''')
replace_once(
    session,
    '''            var result = await session.SendCommandAsync(method, parameters, cancellationToken, extractRuntimeValue).ConfigureAwait(false);\n            RuntimeFlightRecorder.Record("CDP", "CommandCompleted", "success", method, tabId: tab.Id, conversationRef: tab.Url);\n            return result;''',
    '''            var result = await session.SendCommandAsync(method, parameters, cancellationToken, extractRuntimeValue).ConfigureAwait(false);\n            if (recordCommandLifecycle)\n                RuntimeFlightRecorder.Record("CDP", "CommandCompleted", "success", method, tabId: tab.Id, conversationRef: tab.Url);\n            return result;''')
replace_once(
    session,
    '''    public void Prune(IReadOnlyCollection<ChromeTab> liveTabs)''',
    '''    private static bool ShouldRecordCommandLifecycle(string method)\n        => !string.Equals(method, "Runtime.evaluate", StringComparison.Ordinal);\n\n    public void Prune(IReadOnlyCollection<ChromeTab> liveTabs)''')

# 2) Broaden only the structured current-error detector around a visible Retry/Try again control.
chrome = "src/GPTDeskTop/Services/ChromeDevToolsService.cs"
replace_once(chrome, "window.__gptDesktopChatStateCache?.version === 3", "window.__gptDesktopChatStateCache?.version === 4")
replace_once(chrome, "  const version = 3;", "  const version = 4;")
replace_once(
    chrome,
    '''  const findErrorText = () => {\n    const selectors = ['[role="alert"]', '[aria-live="assertive"]', '[data-testid*="error"]', '[data-testid*="retry"]'];\n    for (const selector of selectors) {\n      for (const element of document.querySelectorAll(selector)) {\n        if (!visible(element)) continue;\n        const text = (element.innerText || element.textContent || '').trim();\n        if (text && errorPattern.test(text)) return text;\n      }\n    }\n    return '';\n  };''',
    '''  const findErrorText = () => {\n    const selectors = ['[role="alert"]', '[aria-live="assertive"]', '[data-testid*="error"]', '[data-testid*="retry"]'];\n    for (const selector of selectors) {\n      for (const element of document.querySelectorAll(selector)) {\n        if (!visible(element)) continue;\n        const text = (element.innerText || element.textContent || '').trim();\n        if (text && errorPattern.test(text)) return text;\n      }\n    }\n\n    // ChatGPT sometimes renders the delivery-timeout card without an alert/testid on its\n    // outer container. Inspect only a small ancestor chain around a visible native Retry\n    // control; never scan document.body or conversation text globally.\n    for (const button of document.querySelectorAll('button,[role="button"]')) {\n      if (!visible(button)) continue;\n      const label = `${button.getAttribute('aria-label') || ''} ${button.getAttribute('title') || ''} ${button.innerText || button.textContent || ''}`.trim();\n      if (!/\\bretry\\b|try again|إعادة المحاولة|حاول مرة أخرى/i.test(label)) continue;\n      let container = button;\n      for (let depth = 0; container && depth < 5; depth++, container = container.parentElement) {\n        const text = (container.innerText || container.textContent || '').trim();\n        if (!text || text.length > 600) continue;\n        if (errorPattern.test(text)) return text;\n      }\n    }\n    return '';\n  };''')

# 3) Correlate the whole worker + each poll, and let a structured current error bypass passive unchanged/empty waits.
monitor = "src/GPTDeskTop/Services/ChatGptMonitorService.cs"
replace_once(
    monitor,
    '''        var transientFailures = 0;\n        Activity?.Invoke(monitor.Id,''',
    '''        var transientFailures = 0;\n        using var monitorFlightScope = RuntimeFlightRecorder.BeginScope(monitor.Id);\n        Activity?.Invoke(monitor.Id,''')
replace_once(
    monitor,
    '''            var initial = await GetChatStateWithRetryAsync(monitor.Id, tab, cancellationToken);''',
    '''            ChatPageState initial;\n            using (RuntimeFlightRecorder.BeginScope(monitor.Id, tab.Id, tab.Url))\n                initial = await GetChatStateWithRetryAsync(monitor.Id, tab, cancellationToken);''')
replace_once(
    monitor,
    '''                    var prefix = $"[{monitor.Title}]";\n                    var state = await _chrome.GetChatStateAsync(tab, cancellationToken);''',
    '''                    var prefix = $"[{monitor.Title}]";\n                    using var pollFlightScope = RuntimeFlightRecorder.BeginScope(monitor.Id, tab.Id, tab.Url);\n                    var state = await _chrome.GetChatStateAsync(tab, cancellationToken);''')
replace_once(
    monitor,
    '''                    // A slow/unchanged/empty response is a passive wait state. Time elapsed by itself\n                    // must never mutate the page. Recovery is driven only by explicit current ChatGPT\n                    // error UI or explicit terminal conditions such as conversation/context limits.\n                    if (state.IsGenerating || string.IsNullOrWhiteSpace(text) || (string.Equals(text, lastHandledText, StringComparison.Ordinal) && !messageCountRotationDue)) { candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; continue; }\n                    if (!string.Equals(candidateText, text, StringComparison.Ordinal)) { candidateText = text; candidateSince = DateTimeOffset.UtcNow; Activity?.Invoke(monitor.Id, $"{prefix} New response detected..."); continue; }\n                    if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds) continue;\n                    lastHandledText = text; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;\n                    var isError = !string.IsNullOrWhiteSpace(state.ErrorText); await _database.AddLogAsync("Inbound", string.Empty, text, IsConversationContextLimit(text) ? "ConversationLimit" : isError ? "Error" : "Detected", monitor.Id, tab.Id, monitor.Title, cancellationToken);''',
    '''                    var isError = !string.IsNullOrWhiteSpace(state.ErrorText);\n                    // A slow/unchanged/empty response is a passive wait state only when ChatGPT has no\n                    // current structured error UI. A visible rendered error must be handled even when\n                    // the previous assistant text is unchanged or empty.\n                    if (!isError && (state.IsGenerating || string.IsNullOrWhiteSpace(text) || (string.Equals(text, lastHandledText, StringComparison.Ordinal) && !messageCountRotationDue))) { candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; continue; }\n                    if (!string.Equals(candidateText, text, StringComparison.Ordinal))\n                    {\n                        candidateText = text; candidateSince = DateTimeOffset.UtcNow;\n                        if (isError)\n                        {\n                            RuntimeFlightRecorder.Record("Monitor", "RenderedErrorObserved", "error", IsDeliveryTimeout(text) ? "message-delivery-timeout" : "structured-chatgpt-error");\n                            Activity?.Invoke(monitor.Id, IsDeliveryTimeout(text)\n                                ? $"{prefix} ChatGPT delivery timeout detected; bounded recovery is pending."\n                                : $"{prefix} ChatGPT rendered error detected; bounded recovery is pending.");\n                        }\n                        else Activity?.Invoke(monitor.Id, $"{prefix} New response detected...");\n                        continue;\n                    }\n                    if ((DateTimeOffset.UtcNow - candidateSince).TotalMilliseconds < _config.StableResponseMilliseconds) continue;\n                    lastHandledText = text; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;\n                    await _database.AddLogAsync("Inbound", string.Empty, text, IsConversationContextLimit(text) ? "ConversationLimit" : isError ? "Error" : "Detected", monitor.Id, tab.Id, monitor.Title, cancellationToken);''')
replace_once(
    monitor,
    '''                    if (isError && IsDeliveryTimeout(text))\n                    {\n                        Activity?.Invoke(monitor.Id, $"{prefix} Message delivery timeout saved. Creating a new ChatGPT chat...");''',
    '''                    if (isError && IsDeliveryTimeout(text))\n                    {\n                        RuntimeFlightRecorder.Record("Monitor", "DeliveryTimeoutRecovery", "started", "fresh-chat-handoff");\n                        Activity?.Invoke(monitor.Id, $"{prefix} Message delivery timeout saved. Creating a new ChatGPT chat...");''')
replace_once(
    monitor,
    '''                            Activity?.Invoke(monitor.Id, $"{prefix} Recovery message is still not accepted. Closing the unused recovery tab and retrying later.");\n                            await _database.AddLogAsync("System", recoveryMessage, text, "RecoverySendDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken);''',
    '''                            RuntimeFlightRecorder.Record("Monitor", "DeliveryTimeoutRecovery", "deferred", "continuation-delivery-unverified", tabId: newTab.Id, conversationRef: newTab.Url);\n                            Activity?.Invoke(monitor.Id, $"{prefix} Recovery message is still not accepted. Closing the unused recovery tab and retrying later.");\n                            await _database.AddLogAsync("System", recoveryMessage, text, "RecoverySendDeferred", monitor.Id, newTab.Id, monitor.Title, cancellationToken);''')
replace_once(
    monitor,
    '''                        if (committedRecoveryTab is null)\n                        {\n                            lastHandledText = string.Empty;''',
    '''                        if (committedRecoveryTab is null)\n                        {\n                            RuntimeFlightRecorder.Record("Monitor", "DeliveryTimeoutRecovery", "deferred", "handoff-commit-unverified", tabId: newTab.Id, conversationRef: newTab.Url);\n                            lastHandledText = string.Empty;''')
replace_once(
    monitor,
    '''                        tab = committedRecoveryTab; lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue; Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Recovery chat is now monitored under the same Monitor ID #{monitor.Id}."); await _chrome.CloseTabAsync(oldTab, cancellationToken); continue;''',
    '''                        tab = committedRecoveryTab; lastHandledText = string.Empty; candidateText = string.Empty; candidateSince = DateTimeOffset.MinValue;\n                        RuntimeFlightRecorder.Record("Monitor", "DeliveryTimeoutRecovery", "completed", "fresh-chat-handoff-committed", tabId: committedRecoveryTab.Id, conversationRef: committedRecoveryTab.Url);\n                        Activity?.Invoke(monitor.Id, $"[{monitor.Title}] Recovery chat is now monitored under the same Monitor ID #{monitor.Id}."); await _chrome.CloseTabAsync(oldTab, cancellationToken); continue;''')

# 4) Make stale global diagnostic snapshots explicit instead of presenting old success as current.
inspector = "src/GPTDeskTop/Services/RuntimeInspectorService.cs"
replace_once(
    inspector,
    '''internal sealed record RuntimeInspectorComposerDiagnostics(\n    string Decision,\n    string Reason,\n    DateTimeOffset ObservedAtUtc);''',
    '''internal sealed record RuntimeInspectorComposerDiagnostics(\n    string Decision,\n    string Reason,\n    DateTimeOffset ObservedAtUtc,\n    double AgeSeconds,\n    bool IsStale);''')
replace_once(
    inspector,
    '''internal sealed record RuntimeInspectorVerifiedSendDiagnostics(\n    string Phase,\n    string Reason,\n    int SubmitAttempts,\n    DateTimeOffset ObservedAtUtc);''',
    '''internal sealed record RuntimeInspectorVerifiedSendDiagnostics(\n    string Phase,\n    string Reason,\n    int SubmitAttempts,\n    DateTimeOffset ObservedAtUtc,\n    double AgeSeconds,\n    bool IsStale);''')
replace_once(
    inspector,
    '''    private const int MaxOverflowRows = 25;\n    private const int OverflowToleranceLogicalPixels = 2;''',
    '''    private const int MaxOverflowRows = 25;\n    private const int OverflowToleranceLogicalPixels = 2;\n    private static readonly TimeSpan DiagnosticStaleAfter = TimeSpan.FromMinutes(5);''')
replace_once(
    inspector,
    '''    public static FieldRuntimeSnapshot Capture(Form owner, ChatGptMonitorService monitor)\n    {\n        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();''',
    '''    public static FieldRuntimeSnapshot Capture(Form owner, ChatGptMonitorService monitor)\n    {\n        var capturedUtc = DateTimeOffset.UtcNow;\n        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();''')
replace_once(
    inspector,
    '''        var composerSnapshot = ChatComposerDecisionDiagnostics.Last;\n        var composerDiagnostics = new RuntimeInspectorComposerDiagnostics(\n            composerSnapshot.Decision.ToString(),\n            composerSnapshot.Reason,\n            composerSnapshot.ObservedAtUtc);\n        var verifiedSendSnapshot = VerifiedSendDiagnostics.Last;\n        var verifiedSendDiagnostics = new RuntimeInspectorVerifiedSendDiagnostics(\n            verifiedSendSnapshot.Phase,\n            verifiedSendSnapshot.Reason,\n            verifiedSendSnapshot.SubmitAttempts,\n            verifiedSendSnapshot.ObservedAtUtc);''',
    '''        var composerSnapshot = ChatComposerDecisionDiagnostics.Last;\n        var composerAgeSeconds = Math.Max(0, (capturedUtc - composerSnapshot.ObservedAtUtc).TotalSeconds);\n        var composerDiagnostics = new RuntimeInspectorComposerDiagnostics(\n            composerSnapshot.Decision.ToString(),\n            composerSnapshot.Reason,\n            composerSnapshot.ObservedAtUtc,\n            composerAgeSeconds,\n            composerAgeSeconds > DiagnosticStaleAfter.TotalSeconds);\n        var verifiedSendSnapshot = VerifiedSendDiagnostics.Last;\n        var verifiedSendAgeSeconds = Math.Max(0, (capturedUtc - verifiedSendSnapshot.ObservedAtUtc).TotalSeconds);\n        var verifiedSendDiagnostics = new RuntimeInspectorVerifiedSendDiagnostics(\n            verifiedSendSnapshot.Phase,\n            verifiedSendSnapshot.Reason,\n            verifiedSendSnapshot.SubmitAttempts,\n            verifiedSendSnapshot.ObservedAtUtc,\n            verifiedSendAgeSeconds,\n            verifiedSendAgeSeconds > DiagnosticStaleAfter.TotalSeconds);''')
replace_once(inspector, "            DateTimeOffset.UtcNow,\n            build,", "            capturedUtc,\n            build,")
replace_once(
    inspector,
    '''               $"Composer gate: {composer.Reason} ({composer.Decision}) @ {composer.ObservedAtUtc:O}\\r\\n" +\n               $"Verified send: {verifiedSend.Phase} | attempts: {verifiedSend.SubmitAttempts} | {verifiedSend.Reason} @ {verifiedSend.ObservedAtUtc:O}\\r\\n" +''',
    '''               $"Composer gate: {composer.Reason} ({composer.Decision}) @ {composer.ObservedAtUtc:O} | age: {composer.AgeSeconds:0}s | stale: {(composer.IsStale ? "yes" : "no")}\\r\\n" +\n               $"Verified send: {verifiedSend.Phase} | attempts: {verifiedSend.SubmitAttempts} | {verifiedSend.Reason} @ {verifiedSend.ObservedAtUtc:O} | age: {verifiedSend.AgeSeconds:0}s | stale: {(verifiedSend.IsStale ? "yes" : "no")}\\r\\n" +''')

# 5) Add field-regression tests that pin the exact failures seen in Build 143.
test = Path("tests/GPTDeskTop.RuntimeTests/FieldDeliveryTimeoutRecoveryRegressionTests.cs")
test.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class FieldDeliveryTimeoutRecoveryRegressionTests
{
    [Fact]
    public void RoutineRuntimeEvaluateSuccessesAreSuppressedButFailuresRemainRecorded()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs");
        Assert.Contains("ShouldRecordCommandLifecycle", source, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(method, \"Runtime.evaluate\", StringComparison.Ordinal)", source, StringComparison.Ordinal);
        Assert.Contains("if (recordCommandLifecycle)", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeFlightRecorder.Record(\"CDP\", \"CommandCompleted\", \"failed\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorPollCarriesMonitorAndConversationCorrelationIntoCdpCalls()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        Assert.Contains("RuntimeFlightRecorder.BeginScope(monitor.Id)", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeFlightRecorder.BeginScope(monitor.Id, tab.Id, tab.Url)", source, StringComparison.Ordinal);
        var scope = source.IndexOf("using var pollFlightScope = RuntimeFlightRecorder.BeginScope(monitor.Id, tab.Id, tab.Url)", StringComparison.Ordinal);
        var read = source.IndexOf("var state = await _chrome.GetChatStateAsync(tab, cancellationToken)", scope, StringComparison.Ordinal);
        Assert.True(scope >= 0 && read > scope);
    }

    [Fact]
    public void StructuredRenderedErrorBypassesPassiveUnchangedResponseWait()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var error = source.IndexOf("var isError = !string.IsNullOrWhiteSpace(state.ErrorText)", StringComparison.Ordinal);
        var passive = source.IndexOf("if (!isError && (state.IsGenerating || string.IsNullOrWhiteSpace(text)", StringComparison.Ordinal);
        Assert.True(error >= 0 && passive > error);
        Assert.Contains("RenderedErrorObserved", source, StringComparison.Ordinal);
        Assert.Contains("message-delivery-timeout", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliveryTimeoutUsesFreshChatContinuationNotBlindOriginalResend()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        Assert.Contains("if (isError && IsDeliveryTimeout(text))", source, StringComparison.Ordinal);
        Assert.Contains("DeliveryTimeoutRecovery", source, StringComparison.Ordinal);
        Assert.Contains("SendWhenReadyAsync(monitor.Id, newTab, recoveryMessage", source, StringComparison.Ordinal);
        Assert.Contains("rotationTrigger: \"DeliveryTimeout\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendWhenReadyAsync(monitor.Id, newTab, text", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeFlightRecorder.Record(\"Monitor\", \"RenderedErrorObserved\", \"error\", text", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RetryCardDetectorIsTargetedAndDoesNotScanConversationBodyGlobally()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("version === 4", source, StringComparison.Ordinal);
        Assert.Contains("const version = 4", source, StringComparison.Ordinal);
        Assert.Contains("button,[role=\"button\"]", source, StringComparison.Ordinal);
        Assert.Contains("retry", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("depth < 5", source, StringComparison.Ordinal);
        Assert.Contains("text.length > 600", source, StringComparison.Ordinal);
        Assert.DoesNotContain("document.body?.innerText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("document.body.innerText", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorMarksOldGlobalComposerAndSendDiagnosticsAsStale()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");
        Assert.Contains("DiagnosticStaleAfter = TimeSpan.FromMinutes(5)", source, StringComparison.Ordinal);
        Assert.Contains("double AgeSeconds", source, StringComparison.Ordinal);
        Assert.Contains("bool IsStale", source, StringComparison.Ordinal);
        Assert.Contains("| age: {composer.AgeSeconds:0}s | stale:", source, StringComparison.Ordinal);
        Assert.Contains("| age: {verifiedSend.AgeSeconds:0}s | stale:", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}
''', encoding="utf-8")

print("FIELDERR deterministic integration patch applied.")

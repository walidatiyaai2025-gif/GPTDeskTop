from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SERVICE = ROOT / "src/GPTDeskTop/Services/ChromeDevToolsService.cs"


def replace_required(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    if new in text:
        return
    if old not in text:
        raise RuntimeError(f"Expected text not found in {path}: {old!r}")
    path.write_text(text.replace(old, new), encoding="utf-8")


def replace_method() -> None:
    text = SERVICE.read_text(encoding="utf-8")
    if "fresh-chat-target-anchor" in text:
        return

    start_marker = "    public async Task<bool> EnsureStableConversationTransportAsync("
    end_marker = "    private async Task TryRefreshTabBindingAsync(ChromeTab tab, CancellationToken cancellationToken)"
    start = text.find(start_marker)
    end = text.find(end_marker, start)
    if start < 0 or end <= start:
        raise RuntimeError("Could not locate EnsureStableConversationTransportAsync method boundaries")

    replacement = r'''    public async Task<bool> EnsureStableConversationTransportAsync(
        ChromeTab tab,
        CancellationToken cancellationToken = default,
        int stableReadsRequired = 3)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var originalUrl = tab.Url;
        var hasStableConversationIdentity = RuntimeHealthPresentation.IsChatGptConversationUrl(originalUrl);
        if (!hasStableConversationIdentity && !RuntimeHealthPresentation.IsChatGptTabUrl(originalUrl))
            return false;

        // A brand-new ChatGPT target does not have a durable /c/{conversation-id} until its
        // first user turn is accepted. Requiring a conversation identity here makes every
        // fresh-chat follow-up fail closed before physical input. During this pre-first-turn
        // window the exact target id created by GPTDeskTop is the authority. Once a durable
        // conversation URL exists, the normal conversation-identity recovery rules apply.
        var originalTargetId = tab.Id;
        stableReadsRequired = Math.Clamp(stableReadsRequired, 2, 6);
        var stableBindingKey = string.Empty;
        var stableReads = 0;
        var bindingMode = hasStableConversationIdentity
            ? "same-conversation-read-rebind"
            : "fresh-chat-target-anchor";

        RuntimeFlightRecorder.Record("CDP", "StableBindingRequested", "started", bindingMode, tabId: tab.Id, conversationRef: originalUrl);

        for (var attempt = 1; attempt <= 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var tabs = await GetTabsAsync(cancellationToken).ConfigureAwait(false);
                ChromeTab? current;
                if (hasStableConversationIdentity)
                {
                    current = MonitorDeliveryRecoveryPolicy.FindBestBinding(tabs, tab);
                }
                else
                {
                    current = tabs.FirstOrDefault(candidate =>
                        string.Equals(candidate.Id, originalTargetId, StringComparison.Ordinal)
                        && RuntimeHealthPresentation.IsChatGptTabUrl(candidate.Url));
                }

                var targetMatches = current is not null
                    && (hasStableConversationIdentity
                        ? ChatGptConversationIdentity.IsSame(originalUrl, current.Url)
                        : string.Equals(current.Id, originalTargetId, StringComparison.Ordinal)
                          && RuntimeHealthPresentation.IsChatGptTabUrl(current.Url));

                if (!targetMatches || current is null)
                {
                    stableReads = 0;
                    stableBindingKey = string.Empty;
                    _sessionPool.Invalidate(tab.Id);
                    RuntimeFlightRecorder.Record(
                        "CDP",
                        "StableBindingProbe",
                        "missing",
                        hasStableConversationIdentity ? "same-conversation-target-not-found" : "fresh-chat-target-not-found",
                        tabId: tab.Id,
                        conversationRef: originalUrl);
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, 200 + attempt * 100)), cancellationToken);
                    continue;
                }

                var bindingChanged = !string.Equals(tab.Id, current.Id, StringComparison.Ordinal)
                                     || !string.Equals(tab.WebSocketDebuggerUrl, current.WebSocketDebuggerUrl, StringComparison.Ordinal);
                if (bindingChanged)
                    _sessionPool.Invalidate(tab.Id);

                RebindTab(tab, current);

                const string probeExpression = "(() => ({ href: location.href, ready: document.readyState }))()";
                var probe = await EvaluateAsync(tab, probeExpression, cancellationToken, false);
                var href = probe.TryGetProperty("href", out var hrefElement)
                    ? hrefElement.GetString() ?? string.Empty
                    : string.Empty;
                var ready = probe.TryGetProperty("ready", out var readyElement)
                    ? readyElement.GetString() ?? string.Empty
                    : string.Empty;

                var probeMatches = hasStableConversationIdentity
                    ? ChatGptConversationIdentity.IsSame(originalUrl, href)
                    : string.Equals(tab.Id, originalTargetId, StringComparison.Ordinal)
                      && RuntimeHealthPresentation.IsChatGptTabUrl(href);

                if (!probeMatches || string.Equals(ready, "loading", StringComparison.OrdinalIgnoreCase))
                {
                    stableReads = 0;
                    stableBindingKey = string.Empty;
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                var bindingKey = $"{tab.Id}|{tab.WebSocketDebuggerUrl}";
                if (string.Equals(bindingKey, stableBindingKey, StringComparison.Ordinal))
                {
                    stableReads++;
                }
                else
                {
                    stableBindingKey = bindingKey;
                    stableReads = 1;
                }

                if (stableReads >= stableReadsRequired)
                {
                    RuntimeFlightRecorder.Record(
                        "CDP",
                        "StableBindingCompleted",
                        "ready",
                        $"{bindingMode};stable-reads:{stableReads}",
                        tabId: tab.Id,
                        conversationRef: tab.Url);
                    return true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))
            {
                _sessionPool.Invalidate(tab.Id);
                stableReads = 0;
                stableBindingKey = string.Empty;
                RuntimeFlightRecorder.Record("CDP", "StableBindingProbe", "retry", ex.GetType().Name, tabId: tab.Id, conversationRef: originalUrl);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, 200 + attempt * 100)), cancellationToken);
        }

        RuntimeFlightRecorder.Record("CDP", "StableBindingCompleted", "failed", $"{bindingMode};transport-never-stabilized", tabId: tab.Id, conversationRef: originalUrl);
        return false;
    }

'''
    SERVICE.write_text(text[:start] + replacement + text[end:], encoding="utf-8")


def add_regression_test() -> None:
    path = ROOT / "tests/GPTDeskTop.RuntimeTests/FreshChatTransportAnchorRegressionTests.cs"
    if path.exists():
        return
    path.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class FreshChatTransportAnchorRegressionTests
{
    [Fact]
    public void FreshChatPreFirstTurnUsesExactTargetAnchorInsteadOfDurableConversationRequirement()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var method = Slice(
            source,
            "public async Task<bool> EnsureStableConversationTransportAsync",
            "private async Task TryRefreshTabBindingAsync");

        Assert.Contains("hasStableConversationIdentity", method, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptTabUrl(originalUrl)", method, StringComparison.Ordinal);
        Assert.Contains("var originalTargetId = tab.Id", method, StringComparison.Ordinal);
        Assert.Contains("fresh-chat-target-anchor", method, StringComparison.Ordinal);
        Assert.Contains("string.Equals(candidate.Id, originalTargetId, StringComparison.Ordinal)", method, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptTabUrl(candidate.Url)", method, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptTabUrl(href)", method, StringComparison.Ordinal);
        Assert.Contains("same-conversation-read-rebind", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicalSendStillRequiresStableTransportBeforeComposerAndBeforeNativeInput()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var method = Slice(
            source,
            "public async Task<bool> SendChatMessageAsync",
            "public async Task<bool> SendChatMessageVerifiedAsync");

        Assert.Equal(2, Count(method, "EnsureStableConversationTransportAsync(tab, cancellationToken, stableReadsRequired: 3)"));
        Assert.Contains("cdp-transport-not-stable-before-composer", method, StringComparison.Ordinal);
        Assert.Contains("cdp-transport-not-stable-before-physical-input", method, StringComparison.Ordinal);
        Assert.Contains("TryDispatchNativeSendClickAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryResponseContinuationStillCreatesFreshTargetAndClosesOldOnlyAfterVerifiedHandoff()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs");
        var method = Slice(
            source,
            "private async Task<ChromeTab?> ContinueInFreshChatAfterResponseAsync",
            "private async Task<ChromeTab?> CommitVerifiedConversationHandoffAsync");

        Assert.Contains("CreateNewChatTabAsync", method, StringComparison.Ordinal);
        Assert.Contains("SendWhenReadyAsync(monitor.Id, newTab", method, StringComparison.Ordinal);
        Assert.Contains("if (!sent)", method, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync(newTab", method, StringComparison.Ordinal);
        Assert.Contains("CommitVerifiedConversationHandoffAsync", method, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync(oldTab", method, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts))));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Expected markers '{startMarker}' and '{endMarker}'.");
        return source[start..end];
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }
}
''', encoding="utf-8")


def bump_versions() -> None:
    for rel in [
        "src/GPTDeskTop/GPTDeskTop.csproj",
        "src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj",
        "src/GPTDeskTop.Build/GPTDeskTop.Build.csproj",
        "tests/GPTDeskTop.RuntimeTests/OperatorWorkspaceV2RegressionTests.cs",
    ]:
        replace_required(ROOT / rel, "2.0.7", "2.0.8")

    setup_program = ROOT / "src/GPTDeskTop.Setup/Program.cs"
    text = setup_program.read_text(encoding="utf-8")
    if 'Version = "2.0.8"' not in text:
        import re
        updated, count = re.subn(r'Version = "2\.0\.\d+"', 'Version = "2.0.8"', text, count=1)
        if count != 1:
            raise RuntimeError("Could not update setup Program.cs version constant")
        setup_program.write_text(updated, encoding="utf-8")

    build = ROOT / "src/GPTDeskTop.Build/GPTDeskTop.Build.csproj"
    text = build.read_text(encoding="utf-8")
    if "Fresh Chat pre-first-turn transport" not in text:
        text = text.replace(
            "GPTDeskTop v2.0.8&#x0D;&#x0A;",
            "GPTDeskTop v2.0.8&#x0D;&#x0A;- Fresh Chat pre-first-turn transport is anchored to the exact GPTDeskTop-created Chrome target until /c/{conversation-id} exists&#x0D;&#x0A;",
            1,
        )
        build.write_text(text, encoding="utf-8")


replace_method()
add_regression_test()
bump_versions()
print("GPTDeskTop v2.0.8 fresh-chat CDP anchor hotfix applied")

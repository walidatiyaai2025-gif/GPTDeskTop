from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHROME = ROOT / 'src/GPTDeskTop/Services/ChromeDevToolsService.cs'
MONITOR = ROOT / 'src/GPTDeskTop/Services/ChatGptMonitorService.cs'


def replace_once(path: Path, old: str, new: str, label: str):
    text = path.read_text(encoding='utf-8')
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected exactly one match, found {count}')
    path.write_text(text.replace(old, new, 1), encoding='utf-8')


def replace_all(path: Path, old: str, new: str, label: str):
    text = path.read_text(encoding='utf-8')
    if old not in text:
        if new in text:
            return
        raise RuntimeError(f'{label}: no matches')
    path.write_text(text.replace(old, new), encoding='utf-8')

# Stable same-conversation transport recovery. This is intentionally read/rebind only: no reload.
insert_marker = '''    private async Task TryRefreshTabBindingAsync(ChromeTab tab, CancellationToken cancellationToken)\n'''
stable_method = '''    public async Task<bool> EnsureStableConversationTransportAsync(\n        ChromeTab tab,\n        CancellationToken cancellationToken = default,\n        int stableReadsRequired = 3)\n    {\n        ArgumentNullException.ThrowIfNull(tab);\n        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))\n            return false;\n\n        stableReadsRequired = Math.Clamp(stableReadsRequired, 2, 6);\n        var originalUrl = tab.Url;\n        var stableBindingKey = string.Empty;\n        var stableReads = 0;\n\n        RuntimeFlightRecorder.Record(\"CDP\", \"StableBindingRequested\", \"started\", \"same-conversation-read-rebind\", tabId: tab.Id, conversationRef: originalUrl);\n\n        for (var attempt = 1; attempt <= 12; attempt++)\n        {\n            cancellationToken.ThrowIfCancellationRequested();\n            try\n            {\n                var tabs = await GetTabsAsync(cancellationToken).ConfigureAwait(false);\n                var current = MonitorDeliveryRecoveryPolicy.FindBestBinding(tabs, tab);\n                if (current is null || !ChatGptConversationIdentity.IsSame(originalUrl, current.Url))\n                {\n                    stableReads = 0;\n                    stableBindingKey = string.Empty;\n                    _sessionPool.Invalidate(tab.Id);\n                    RuntimeFlightRecorder.Record(\"CDP\", \"StableBindingProbe\", \"missing\", \"same-conversation-target-not-found\", tabId: tab.Id, conversationRef: originalUrl);\n                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, 200 + attempt * 100)), cancellationToken);\n                    continue;\n                }\n\n                var bindingChanged = !string.Equals(tab.Id, current.Id, StringComparison.Ordinal)\n                                     || !string.Equals(tab.WebSocketDebuggerUrl, current.WebSocketDebuggerUrl, StringComparison.Ordinal);\n                if (bindingChanged)\n                    _sessionPool.Invalidate(tab.Id);\n\n                RebindTab(tab, current);\n\n                const string probeExpression = \"(() => ({ href: location.href, ready: document.readyState }))()\";\n                var probe = await EvaluateAsync(tab, probeExpression, cancellationToken, false);\n                var href = probe.TryGetProperty(\"href\", out var hrefElement)\n                    ? hrefElement.GetString() ?? string.Empty\n                    : string.Empty;\n                var ready = probe.TryGetProperty(\"ready\", out var readyElement)\n                    ? readyElement.GetString() ?? string.Empty\n                    : string.Empty;\n\n                if (!ChatGptConversationIdentity.IsSame(originalUrl, href)\n                    || string.Equals(ready, \"loading\", StringComparison.OrdinalIgnoreCase))\n                {\n                    stableReads = 0;\n                    stableBindingKey = string.Empty;\n                    await Task.Delay(250, cancellationToken);\n                    continue;\n                }\n\n                var bindingKey = $\"{tab.Id}|{tab.WebSocketDebuggerUrl}\";\n                if (string.Equals(bindingKey, stableBindingKey, StringComparison.Ordinal))\n                {\n                    stableReads++;\n                }\n                else\n                {\n                    stableBindingKey = bindingKey;\n                    stableReads = 1;\n                }\n\n                if (stableReads >= stableReadsRequired)\n                {\n                    RuntimeFlightRecorder.Record(\"CDP\", \"StableBindingCompleted\", \"ready\", $\"stable-reads:{stableReads}\", tabId: tab.Id, conversationRef: tab.Url);\n                    return true;\n                }\n            }\n            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)\n            {\n                throw;\n            }\n            catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))\n            {\n                _sessionPool.Invalidate(tab.Id);\n                stableReads = 0;\n                stableBindingKey = string.Empty;\n                RuntimeFlightRecorder.Record(\"CDP\", \"StableBindingProbe\", \"retry\", ex.GetType().Name, tabId: tab.Id, conversationRef: originalUrl);\n            }\n\n            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, 200 + attempt * 100)), cancellationToken);\n        }\n\n        RuntimeFlightRecorder.Record(\"CDP\", \"StableBindingCompleted\", \"failed\", \"transport-never-stabilized\", tabId: tab.Id, conversationRef: originalUrl);\n        return false;\n    }\n\n'''
text = CHROME.read_text(encoding='utf-8')
if 'public async Task<bool> EnsureStableConversationTransportAsync(' not in text:
    if text.count(insert_marker) != 1:
        raise RuntimeError('stable transport insertion marker missing')
    CHROME.write_text(text.replace(insert_marker, stable_method + insert_marker, 1), encoding='utf-8')

replace_once(
    CHROME,
    '''        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))\n        {\n            _sessionPool.Invalidate(tab.Id);\n            await TryRefreshTabBindingAsync(tab, cancellationToken).ConfigureAwait(false);\n            return (false, 0, string.Empty);\n        }\n''',
    '''        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))\n        {\n            _sessionPool.Invalidate(tab.Id);\n            await EnsureStableConversationTransportAsync(tab, cancellationToken, stableReadsRequired: 3).ConfigureAwait(false);\n            return (false, 0, string.Empty);\n        }\n''',
    'snapshot stable recovery')

s = CHROME.read_text(encoding='utf-8')
start = s.index('    public async Task<bool> SendChatMessageAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)')
end = s.index('    public async Task<bool> SendChatMessageVerifiedAsync', start)
new_method = '''    public async Task<bool> SendChatMessageAsync(ChromeTab tab, string message, CancellationToken cancellationToken = default)\n    {\n        if (!await EnsureStableConversationTransportAsync(tab, cancellationToken, stableReadsRequired: 3).ConfigureAwait(false))\n        {\n            VerifiedSendDiagnostics.Record(\"Deferred\", \"cdp-transport-not-stable-before-composer\", 0);\n            return false;\n        }\n\n        try\n        {\n            var preparationDecision = await ReadComposerDecisionAsync(tab, requireSendReady: false, cancellationToken);\n            if (preparationDecision != ComposerAutomationDecision.ReadyToPrepare)\n                return false;\n\n            var textLiteral = JsonSerializer.Serialize(message);\n            var setEditorExpression = $$\"\"\"\n            (() => {\n              const text = {{textLiteral}};\n              const visible = element => {\n                if (!element) return false;\n                const rect = element.getBoundingClientRect();\n                const style = getComputedStyle(element);\n                return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';\n              };\n              const stop = document.querySelector('button[data-testid=\"stop-button\"]');\n              if (visible(stop)) return false;\n              const editor = document.querySelector('#prompt-textarea') || document.querySelector('textarea[placeholder]');\n              if (!editor || !visible(editor) || editor.matches(':disabled,[aria-disabled=\"true\"]')) return false;\n              editor.focus();\n              if (editor instanceof HTMLTextAreaElement || editor instanceof HTMLInputElement) {\n                const setter = Object.getOwnPropertyDescriptor(editor instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype, 'value')?.set;\n                setter?.call(editor, text);\n                editor.dispatchEvent(new Event('input', { bubbles: true }));\n                editor.dispatchEvent(new Event('change', { bubbles: true }));\n              } else {\n                const selection = window.getSelection();\n                const range = document.createRange();\n                range.selectNodeContents(editor);\n                selection?.removeAllRanges();\n                selection?.addRange(range);\n                document.execCommand('insertText', false, text);\n                editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: text }));\n              }\n              return true;\n            })()\n            \"\"\";\n\n            var editorPrepared = await EvaluateAsync(tab, setEditorExpression, cancellationToken, false);\n            if (editorPrepared.ValueKind != JsonValueKind.True) return false;\n\n            for (var readinessAttempt = 0; readinessAttempt < 6; readinessAttempt++)\n            {\n                cancellationToken.ThrowIfCancellationRequested();\n                var submitDecision = await ReadComposerDecisionAsync(tab, requireSendReady: true, cancellationToken);\n                if (submitDecision == ComposerAutomationDecision.ReadyToSend)\n                    break;\n                if (submitDecision is ComposerAutomationDecision.DeferWhileGenerating or ComposerAutomationDecision.DeferForRenderedError)\n                    return false;\n                if (readinessAttempt == 5) return false;\n                await Task.Delay(150, cancellationToken);\n            }\n\n            if (!await EnsureStableConversationTransportAsync(tab, cancellationToken, stableReadsRequired: 3).ConfigureAwait(false))\n            {\n                VerifiedSendDiagnostics.Record(\"Deferred\", \"cdp-transport-not-stable-before-physical-input\", 0);\n                return false;\n            }\n\n            var composerBeforeSubmit = await ReadComposerTextAsync(tab, cancellationToken);\n            var finalSubmitDecision = await ReadComposerDecisionAsync(tab, requireSendReady: true, cancellationToken);\n            if (!composerBeforeSubmit.Present\n                || !ComposerEvidenceTextEquals(composerBeforeSubmit.Text, message)\n                || finalSubmitDecision != ComposerAutomationDecision.ReadyToSend)\n            {\n                VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"composer-revalidation-required-after-cdp-rebind\", 0);\n                return false;\n            }\n        }\n        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)\n        {\n            throw;\n        }\n        catch (Exception ex) when (IsRecoverableMonitorTransportException(ex))\n        {\n            _sessionPool.Invalidate(tab.Id);\n            await EnsureStableConversationTransportAsync(tab, cancellationToken, stableReadsRequired: 3).ConfigureAwait(false);\n            VerifiedSendDiagnostics.Record(\"RetryAuthorized\", \"pre-submit-cdp-recovered-before-physical-input\", 0);\n            return false;\n        }\n\n        var submitted = await TryDispatchNativeSendClickAsync(tab, cancellationToken);\n        if (!submitted) return false;\n        try\n        {\n            await EvaluateAsync(tab, \"(() => { try { window.__gptDesktopChatStateCache?.autoFollow?.rearm?.('automation-send'); } catch {} return true; })()\", cancellationToken, false);\n        }\n        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))\n        {\n        }\n        return true;\n    }\n\n'''
if 'pre-submit-cdp-recovered-before-physical-input' not in s:
    CHROME.write_text(s[:start] + new_method + s[end:], encoding='utf-8')

replace_once(
    MONITOR,
    '''            catch (Exception ex) when (IsTransientChromeException(ex))\n            {\n                if (attempt <= 3 || attempt % 12 == 0)\n                    Activity?.Invoke(monitorId, $\"Chrome/CDP connection retry {attempt}: {ex.GetType().Name}. Monitor remains active and will keep self-healing.\");\n                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5000, 500 * attempt)), cancellationToken);\n            }\n''',
    '''            catch (Exception ex) when (IsTransientChromeException(ex))\n            {\n                if (attempt <= 3 || attempt % 12 == 0)\n                    Activity?.Invoke(monitorId, $\"Chrome/CDP transport disconnect retry {attempt}: {ex.GetType().Name}. Rebinding the same conversation target.\");\n\n                var recovered = await _chrome.EnsureStableConversationTransportAsync(\n                    tab,\n                    cancellationToken,\n                    stableReadsRequired: 3);\n                if (recovered)\n                {\n                    Activity?.Invoke(monitorId, \"Chrome/CDP recovery complete: same conversation target is stable.\");\n                    attempt = 0;\n                    await Task.Delay(150, cancellationToken);\n                    continue;\n                }\n\n                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5000, 500 * attempt)), cancellationToken);\n            }\n''',
    'monitor active rebind')

for relative in [
    'src/GPTDeskTop/GPTDeskTop.csproj',
    'src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj',
    'src/GPTDeskTop.Setup/Program.cs',
    'src/GPTDeskTop.Build/GPTDeskTop.Build.csproj',
    'tests/GPTDeskTop.RuntimeTests/OperatorWorkspaceV2RegressionTests.cs',
    'tests/GPTDeskTop.RuntimeTests/PhysicalSubmitAcceptanceRegressionTests.cs',
]:
    path = ROOT / relative
    replace_all(path, '2.0.5', '2.0.6', f'version {relative}')

test = ROOT / 'tests/GPTDeskTop.RuntimeTests/CdpTransportStabilityRegressionTests.cs'
test.write_text(r'''namespace GPTDeskTop.RuntimeTests;

public sealed class CdpTransportStabilityRegressionTests
{
    [Fact]
    public void StableTransportRecoveryRebindsSameConversationWithoutReloading()
    {
        var chrome = ChromeSource();
        var method = Slice(chrome,
            "public async Task<bool> EnsureStableConversationTransportAsync",
            "private async Task TryRefreshTabBindingAsync");

        Assert.Contains("stableReadsRequired = Math.Clamp", method, StringComparison.Ordinal);
        Assert.Contains("MonitorDeliveryRecoveryPolicy.FindBestBinding", method, StringComparison.Ordinal);
        Assert.Contains("ChatGptConversationIdentity.IsSame(originalUrl, current.Url)", method, StringComparison.Ordinal);
        Assert.Contains("_sessionPool.Invalidate(tab.Id)", method, StringComparison.Ordinal);
        Assert.Contains("stableReads >= stableReadsRequired", method, StringComparison.Ordinal);
        Assert.Contains("StableBindingCompleted", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Page.reload", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadTabAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicalSubmitRequiresStableTransportBeforeAndAfterComposerPreparation()
    {
        var chrome = ChromeSource();
        var method = Slice(chrome,
            "public async Task<bool> SendChatMessageAsync",
            "public async Task<bool> SendChatMessageVerifiedAsync");

        Assert.True(Count(method, "EnsureStableConversationTransportAsync") >= 3);
        Assert.Contains("cdp-transport-not-stable-before-composer", method, StringComparison.Ordinal);
        Assert.Contains("cdp-transport-not-stable-before-physical-input", method, StringComparison.Ordinal);
        Assert.Contains("composer-revalidation-required-after-cdp-rebind", method, StringComparison.Ordinal);
        Assert.Contains("pre-submit-cdp-recovered-before-physical-input", method, StringComparison.Ordinal);
        Assert.Contains("TryDispatchNativeSendClickAsync", method, StringComparison.Ordinal);

        var physicalBoundary = method.IndexOf("var submitted = await TryDispatchNativeSendClickAsync", StringComparison.Ordinal);
        var preSubmitCatch = method.IndexOf("pre-submit-cdp-recovered-before-physical-input", StringComparison.Ordinal);
        Assert.True(physicalBoundary > preSubmitCatch, "Recoverable pre-submit transport failures must be handled before native physical input.");
    }

    [Fact]
    public void MonitorTransportRetryActivelyRebindsAndClearsDegradedPresentationOnlyAfterRecovery()
    {
        var monitor = MonitorSource();
        var method = Slice(monitor,
            "private async Task<ChatPageState> GetChatStateWithRetryAsync",
            "private static bool IsTransientChromeException");

        Assert.Contains("Chrome/CDP transport disconnect retry", method, StringComparison.Ordinal);
        Assert.Contains("EnsureStableConversationTransportAsync", method, StringComparison.Ordinal);
        Assert.Contains("Chrome/CDP recovery complete", method, StringComparison.Ordinal);
        Assert.Contains("attempt = 0", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseIdentityIsV206()
    {
        var root = Root();
        Assert.Contains("<Version>2.0.6</Version>", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "GPTDeskTop.csproj")), StringComparison.Ordinal);
        Assert.Contains("<Version>2.0.6</Version>", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop.Setup", "GPTDeskTop.Setup.csproj")), StringComparison.Ordinal);
        Assert.Contains("internal const string Version = \"2.0.6\";", File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop.Setup", "Program.cs")), StringComparison.Ordinal);
    }

    private static string ChromeSource() => File.ReadAllText(Path.Combine(Root(), "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));
    private static string MonitorSource() => File.ReadAllText(Path.Combine(Root(), "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs"));
    private static string Root() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

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
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
''', encoding='utf-8')

build = ROOT / 'src/GPTDeskTop.Build/GPTDeskTop.Build.csproj'
b = build.read_text(encoding='utf-8')
if 'Same-conversation CDP target/session stabilization' not in b:
    b = b.replace('GPTDeskTop v2.0.6&#x0D;&#x0A;- Hydration-stable', 'GPTDeskTop v2.0.6&#x0D;&#x0A;- Same-conversation CDP target/session stabilization before physical submit&#x0D;&#x0A;- Broken CDP sessions are retired and rebound without page reload&#x0D;&#x0A;- Hydration-stable', 1)
    build.write_text(b, encoding='utf-8')

print('v2.0.6 CDP transport stability hotfix applied')

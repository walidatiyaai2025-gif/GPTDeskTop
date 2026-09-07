from pathlib import Path


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one match, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


def replace_exact(path: str, old: str, new: str, expected: int) -> None:
    text = read(path)
    count = text.count(old)
    if count != expected:
        raise SystemExit(f"{path}: expected {expected} matches, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new))


def replace_block(path: str, start_marker: str, end_marker: str, replacement: str) -> None:
    text = read(path)
    start_count = text.count(start_marker)
    end_count = text.count(end_marker)
    if start_count != 1 or end_count < 1:
        raise SystemExit(
            f"{path}: block markers invalid; start={start_count}, end={end_count}, "
            f"start_marker={start_marker[:100]!r}, end_marker={end_marker[:100]!r}"
        )
    start = text.index(start_marker)
    end = text.index(end_marker, start + len(start_marker))
    write(path, text[:start] + replacement + text[end:])


pool = "src/GPTDeskTop/Services/ChromeDevToolsSessionPool.cs"
replace_once(
    pool,
    "    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(12);",
    "    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(12);\n"
    "    internal static readonly TimeSpan PassiveRuntimeEvaluateTimeout = TimeSpan.FromSeconds(30);",
)
replace_once(
    pool,
    "        CancellationToken cancellationToken,\n        bool extractRuntimeValue = false)\n    {",
    "        CancellationToken cancellationToken,\n        bool extractRuntimeValue = false,\n"
    "        TimeSpan? commandTimeout = null)\n    {",
)
replace_once(
    pool,
    "        return SendInstrumentedAsync(session, tab, method, parameters, cancellationToken, extractRuntimeValue, recordCommandLifecycle);",
    "        return SendInstrumentedAsync(session, tab, method, parameters, cancellationToken, extractRuntimeValue, commandTimeout ?? CommandTimeout, recordCommandLifecycle);",
)
replace_once(
    pool,
    "        CancellationToken cancellationToken,\n        bool extractRuntimeValue,\n        bool recordCommandLifecycle)",
    "        CancellationToken cancellationToken,\n        bool extractRuntimeValue,\n"
    "        TimeSpan commandTimeout,\n        bool recordCommandLifecycle)",
)
replace_once(
    pool,
    "            var result = await session.SendCommandAsync(method, parameters, cancellationToken, extractRuntimeValue).ConfigureAwait(false);",
    "            var result = await session.SendCommandAsync(method, parameters, cancellationToken, commandTimeout, extractRuntimeValue).ConfigureAwait(false);",
)
replace_once(
    pool,
    "            CancellationToken cancellationToken,\n            bool extractRuntimeValue)\n        {",
    "            CancellationToken cancellationToken,\n            TimeSpan commandTimeout,\n"
    "            bool extractRuntimeValue)\n        {",
)
replace_once(
    pool,
    "_commandGate.WaitAsync(CommandTimeout, cancellationToken)",
    "_commandGate.WaitAsync(commandTimeout, cancellationToken)",
)
replace_once(pool, "commandCts.CancelAfter(CommandTimeout);", "commandCts.CancelAfter(commandTimeout);")
replace_exact(pool, "CommandTimeout.TotalSeconds", "commandTimeout.TotalSeconds", 2)

chrome = "src/GPTDeskTop/Services/ChromeDevToolsService.cs"
replace_once(
    chrome,
    '''    private async Task<ChatPageState> ReadChatStateCoreAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        var value = await EvaluateAsync(tab, ChatStateReadExpression, cancellationToken, false);
        if (value.ValueKind == JsonValueKind.Null)
            value = await EvaluateAsync(tab, BuildChatStateInstallExpression(), cancellationToken, false);''',
    '''    public Task<ChatPageState> ReadChatStatePassiveAsync(ChromeTab tab, CancellationToken cancellationToken = default)
        => ReadChatStateCoreAsync(tab, cancellationToken, ChromeDevToolsSessionPool.PassiveRuntimeEvaluateTimeout);

    private async Task<ChatPageState> ReadChatStateCoreAsync(
        ChromeTab tab,
        CancellationToken cancellationToken,
        TimeSpan? commandTimeout = null)
    {
        var value = await EvaluateAsync(tab, ChatStateReadExpression, cancellationToken, false, commandTimeout);
        if (value.ValueKind == JsonValueKind.Null)
            value = await EvaluateAsync(tab, BuildChatStateInstallExpression(), cancellationToken, false, commandTimeout);''',
)
replace_block(
    chrome,
    "    public Process LaunchMonitorChrome(string? startUrl = null)\n",
    "    public async Task<ChromeTab> CreateTabAsync",
    "",
)
replace_block(
    chrome,
    "    private async Task<bool> RecoverMonitorTabAsync(ChromeTab tab, CancellationToken cancellationToken)\n",
    "    private async Task<bool> RefreshConversationTabAsync",
    '''    private async Task<bool> RecoverMonitorTabAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        // Passive recovery only: retire the CDP session and rebind to an already-live target
        // for the exact same conversation. Never reload, create/close a tab, or launch Chrome.
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            return false;

        _sessionPool.Invalidate(tab.Id);
        var replacement = await TryFindConversationTabAsync(tab.Url, cancellationToken).ConfigureAwait(false);
        if (replacement is null)
            return false;

        RebindTab(tab, replacement);
        return true;
    }
''',
)
replace_block(
    chrome,
    "    private async Task<bool> RefreshStuckComposerAsync(ChromeTab tab, CancellationToken cancellationToken)\n",
    "    public async Task<bool> SendChatMessageAsync",
    '''    private async Task<bool> RefreshStuckComposerAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        // Keep uncertain-send recovery passive. Rebind the existing target/session only;
        // never Page.reload or create another target behind the operator's back.
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            return false;

        var originalUrl = tab.Url;
        _sessionPool.Invalidate(tab.Id);
        await TryRefreshTabBindingAsync(tab, cancellationToken).ConfigureAwait(false);
        if (!ChatGptConversationIdentity.IsSame(originalUrl, tab.Url))
            return false;

        try
        {
            var readiness = await ReadComposerReadinessAsync(tab, cancellationToken).ConfigureAwait(false);
            return !readiness.IsGenerating
                   && readiness.EditorPresent
                   && readiness.EditorEnabled
                   && !readiness.HasRenderedError;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRecoverableMonitorTransportException(ex))
        {
            return false;
        }
    }
''',
)
replace_once(
    chrome,
    '    private async Task<JsonElement> EvaluateAsync(ChromeTab tab, string expression, CancellationToken cancellationToken, bool awaitPromise) { for (var attempt = 1; attempt <= 3; attempt++) { try { return await SendCommandAsync(tab, "Runtime.evaluate", new { expression, returnByValue = true, awaitPromise, userGesture = true }, cancellationToken, true); } catch (InvalidOperationException ex) when (IsTransientPromiseCollected(ex) && attempt < 3) { await Task.Delay(120 * attempt, cancellationToken); } } throw new InvalidOperationException("Runtime.evaluate failed after transient retry attempts."); }',
    '    private async Task<JsonElement> EvaluateAsync(ChromeTab tab, string expression, CancellationToken cancellationToken, bool awaitPromise, TimeSpan? commandTimeout = null) { for (var attempt = 1; attempt <= 3; attempt++) { try { return await SendCommandAsync(tab, "Runtime.evaluate", new { expression, returnByValue = true, awaitPromise, userGesture = true }, cancellationToken, true, commandTimeout); } catch (InvalidOperationException ex) when (IsTransientPromiseCollected(ex) && attempt < 3) { await Task.Delay(120 * attempt, cancellationToken); } } throw new InvalidOperationException("Runtime.evaluate failed after transient retry attempts."); }',
)
replace_once(
    chrome,
    '    private Task<JsonElement> SendCommandAsync(ChromeTab tab, string method, object parameters, CancellationToken cancellationToken, bool extractRuntimeValue = false)\n        => _sessionPool.SendCommandAsync(tab, method, parameters, cancellationToken, extractRuntimeValue);',
    '    private Task<JsonElement> SendCommandAsync(ChromeTab tab, string method, object parameters, CancellationToken cancellationToken, bool extractRuntimeValue = false, TimeSpan? commandTimeout = null)\n        => _sessionPool.SendCommandAsync(tab, method, parameters, cancellationToken, extractRuntimeValue, commandTimeout);',
)

runner = "src/GPTDeskTop/Services/SimpleMonitorRunner.cs"
replace_once(runner, "using System.Reflection;\n", "")
replace_block(
    runner,
    "    private static readonly MethodInfo PassiveStateReader = typeof(ChromeDevToolsService).GetMethod(\n",
    "    private readonly object _sync = new();",
    "",
)
replace_once(
    runner,
    '''    int PendingMessages,
    int PassiveReadRetries,
    string LastCdpEvent,
    string LastError);''',
    '''    int PendingMessages,
    int PassiveReadRetries,
    int ConsecutivePassiveReadFailures,
    string LastRecovery,
    string LastTransientError,
    string LastCdpEvent,
    string LastError);''',
)
replace_once(
    runner,
    "    private int _passiveReadRetries;\n    private int _sentMessages;",
    "    private int _passiveReadRetries;\n    private int _consecutivePassiveReadFailures;\n"
    "    private string _lastRecovery = \"Healthy\";\n    private string _lastTransientError = string.Empty;\n"
    "    private int _sentMessages;",
)
replace_once(
    runner,
    "            _passiveReadRetries = 0;\n            _sentMessages =",
    "            _passiveReadRetries = 0;\n            _consecutivePassiveReadFailures = 0;\n"
    "            _lastRecovery = \"Healthy\";\n            _lastTransientError = string.Empty;\n"
    "            _sentMessages =",
)
replace_once(
    runner,
    '''                var state = await InvokePassiveStateReaderAsync(chrome, tab, cancellationToken).ConfigureAwait(false);
                if (attempt > 1) _lastCdpEvent = "Runtime.evaluate recovered";
                PublishInspector("ReadingChatState");
                return state;''',
    '''                var state = await InvokePassiveStateReaderAsync(chrome, tab, cancellationToken).ConfigureAwait(false);
                _consecutivePassiveReadFailures = 0;
                _lastRecovery = attempt > 1 ? "Recovered" : "Healthy";
                _lastError = string.Empty;
                if (attempt > 1) _lastCdpEvent = "Runtime.evaluate recovered";
                PublishInspector("ReadingChatState");
                return state;''',
)
replace_once(
    runner,
    '''                _passiveReadRetries++;
                _lastError = ex.Message;''',
    '''                _passiveReadRetries++;
                _consecutivePassiveReadFailures++;
                _lastRecovery = "Retrying";
                _lastTransientError = ex.Message;
                _lastError = ex.Message;''',
)
replace_once(
    runner,
    '''            catch (Exception ex) when (IsTransientRuntimeEvaluateTimeout(ex))
            {
                _lastError = ex.Message;''',
    '''            catch (Exception ex) when (IsTransientRuntimeEvaluateTimeout(ex))
            {
                _consecutivePassiveReadFailures++;
                _lastRecovery = "Exhausted";
                _lastTransientError = ex.Message;
                _lastError = ex.Message;''',
)
replace_block(
    runner,
    "    private static Task<ChatPageState> InvokePassiveStateReaderAsync(\n",
    "    private static bool IsTransientRuntimeEvaluateTimeout",
    '''    private static Task<ChatPageState> InvokePassiveStateReaderAsync(
        ChromeDevToolsService chrome,
        ChromeTab tab,
        CancellationToken cancellationToken)
        => SimpleMonitorPassiveReadGate.RunAsync(
            () => chrome.ReadChatStatePassiveAsync(tab, cancellationToken),
            cancellationToken);

''',
)
replace_once(
    runner,
    '''            _pendingMessages,
            _passiveReadRetries,
            _lastCdpEvent,
            _lastError));''',
    '''            _pendingMessages,
            _passiveReadRetries,
            _consecutivePassiveReadFailures,
            _lastRecovery,
            _lastTransientError,
            _lastCdpEvent,
            _lastError));''',
)

form = "src/GPTDeskTop/UI/SimpleMonitorForm.cs"
replace_once(
    form,
    '    private readonly Label _inspectorRetries = InspectorValue("CDP retries: 0");',
    '    private readonly Label _inspectorRetries = InspectorValue("Total CDP retries: 0  •  Consecutive: 0");',
)
replace_once(
    form,
    '''            _inspectorRetries.Text = $"CDP retries: {snapshot.PassiveReadRetries}";
            _inspectorCdp.Text = $"Last CDP: {snapshot.LastCdpEvent}";
            _inspectorError.Text = string.IsNullOrWhiteSpace(snapshot.LastError) ? "Last error: —" : $"Last error: {snapshot.LastError}";
            _inspectorError.ForeColor = string.IsNullOrWhiteSpace(snapshot.LastError) ? FluentTheme.Muted : Color.OrangeRed;''',
    '''            _inspectorRetries.Text = $"Total CDP retries: {snapshot.PassiveReadRetries}  •  Consecutive: {snapshot.ConsecutivePassiveReadFailures}";
            _inspectorCdp.Text = $"Last CDP: {snapshot.LastCdpEvent}  •  Recovery: {snapshot.LastRecovery}";
            _inspectorError.Text = !string.IsNullOrWhiteSpace(snapshot.LastError)
                ? $"Current error: {snapshot.LastError}"
                : string.IsNullOrWhiteSpace(snapshot.LastTransientError)
                    ? "Current error: —"
                    : $"Last transient (recovered): {snapshot.LastTransientError}";
            _inspectorError.ForeColor = !string.IsNullOrWhiteSpace(snapshot.LastError) ? Color.OrangeRed : FluentTheme.Muted;''',
)

gate = ".github/workflows/qa-monitor-start-chrome-launch.yml"
replace_once(
    gate,
    "          Write-Host 'PASS: Monitor Only can launch Chrome only from the explicit Start Monitor path.'",
    r'''          $runnerPath = 'src/GPTDeskTop/Services/SimpleMonitorRunner.cs'
          $chromePath = 'src/GPTDeskTop/Services/ChromeDevToolsService.cs'
          $poolPath = 'src/GPTDeskTop/Services/ChromeDevToolsSessionPool.cs'
          $runner = Get-Content $runnerPath -Raw
          $chrome = Get-Content $chromePath -Raw
          $pool = Get-Content $poolPath -Raw

          if ($runner -match 'System\.Reflection|PassiveStateReader\.Invoke|GetChatStateAsync\s*\(|ReloadTabAsync\s*\(|LaunchMonitorChrome\s*\(') {
            throw 'Monitor Only runner regained a reflective or browser-mutating passive-read path.'
          }
          if ($runner -notmatch 'ReadChatStatePassiveAsync\s*\(') {
            throw 'Monitor Only runner is not using the explicit passive chat-state API.'
          }
          if ($pool -notmatch 'PassiveRuntimeEvaluateTimeout\s*=\s*TimeSpan\.FromSeconds\(30\)') {
            throw 'Passive Runtime.evaluate timeout is not independently pinned to 30 seconds.'
          }
          if ($chrome -match 'Process\.Start\s*\(|LaunchMonitorChrome\s*\(') {
            throw 'ChromeDevToolsService still contains a browser-launch boundary. Start Monitor must own the only launch.'
          }

          $recoverStart = $chrome.IndexOf('private async Task<bool> RecoverMonitorTabAsync', [StringComparison]::Ordinal)
          $recoverEnd = $chrome.IndexOf('private async Task<bool> RefreshConversationTabAsync', [StringComparison]::Ordinal)
          if ($recoverStart -lt 0 -or $recoverEnd -le $recoverStart) { throw 'Could not isolate RecoverMonitorTabAsync.' }
          $recoverBody = $chrome.Substring($recoverStart, $recoverEnd - $recoverStart)
          if ($recoverBody -match 'ReloadTabAsync|CreateTabAsync|ReopenConversationTabAsync|RefreshConversationTabAsync|CloseTabAsync|LaunchMonitorChrome') {
            throw 'RecoverMonitorTabAsync contains a browser mutation.'
          }

          $stuckStart = $chrome.IndexOf('private async Task<bool> RefreshStuckComposerAsync', [StringComparison]::Ordinal)
          $stuckEnd = $chrome.IndexOf('public async Task<bool> SendChatMessageAsync', [StringComparison]::Ordinal)
          if ($stuckStart -lt 0 -or $stuckEnd -le $stuckStart) { throw 'Could not isolate RefreshStuckComposerAsync.' }
          $stuckBody = $chrome.Substring($stuckStart, $stuckEnd - $stuckStart)
          if ($stuckBody -match 'ReloadTabAsync|Page\.reload|CreateTabAsync|CloseTabAsync|LaunchMonitorChrome') {
            throw 'Stuck-composer recovery contains a hidden browser mutation.'
          }

          Write-Host 'PASS: Start-only launch, passive timeout, passive recovery and diagnostics contracts are enforced.' ''',
)

test = Path("tests/GPTDeskTop.RuntimeTests/MonitorOnlyPassiveCdpHardeningRegressionTests.cs")
test.write_text(
    '''namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorOnlyPassiveCdpHardeningRegressionTests
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
    public void PassiveRuntimeEvaluateHasIndependentThirtySecondTimeout()
    {
        var pool = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs");
        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.Contains("PassiveRuntimeEvaluateTimeout = TimeSpan.FromSeconds(30)", pool, StringComparison.Ordinal);
        Assert.Contains("ReadChatStatePassiveAsync", chrome, StringComparison.Ordinal);
        Assert.Contains("ChromeDevToolsSessionPool.PassiveRuntimeEvaluateTimeout", chrome, StringComparison.Ordinal);
    }

    [Fact]
    public void PassiveReaderUsesTypedApiAndClearsCurrentErrorAfterRecovery()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        Assert.DoesNotContain("System.Reflection", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("PassiveStateReader.Invoke", runner, StringComparison.Ordinal);
        Assert.Contains("chrome.ReadChatStatePassiveAsync", runner, StringComparison.Ordinal);
        Assert.Contains("_lastError = string.Empty", runner, StringComparison.Ordinal);
        Assert.Contains("_consecutivePassiveReadFailures = 0", runner, StringComparison.Ordinal);
        Assert.Contains("_lastRecovery = attempt > 1 ? \"Recovered\" : \"Healthy\"", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryCannotReloadCreateOrRelaunchBrowser()
    {
        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        Assert.DoesNotContain("Process.Start(", chrome, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchMonitorChrome(", chrome, StringComparison.Ordinal);

        var recoverStart = chrome.IndexOf("private async Task<bool> RecoverMonitorTabAsync", StringComparison.Ordinal);
        var recoverEnd = chrome.IndexOf("private async Task<bool> RefreshConversationTabAsync", StringComparison.Ordinal);
        Assert.True(recoverStart >= 0 && recoverEnd > recoverStart);
        var recover = chrome[recoverStart..recoverEnd];
        Assert.DoesNotContain("ReloadTabAsync", recover, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateTabAsync", recover, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseTabAsync", recover, StringComparison.Ordinal);

        var stuckStart = chrome.IndexOf("private async Task<bool> RefreshStuckComposerAsync", StringComparison.Ordinal);
        var stuckEnd = chrome.IndexOf("public async Task<bool> SendChatMessageAsync", StringComparison.Ordinal);
        Assert.True(stuckStart >= 0 && stuckEnd > stuckStart);
        var stuck = chrome[stuckStart..stuckEnd];
        Assert.DoesNotContain("ReloadTabAsync", stuck, StringComparison.Ordinal);
        Assert.DoesNotContain("Page.reload", stuck, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectorSeparatesTotalRetriesConsecutiveFailuresAndRecoveredTransientError()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var form = ReadSource("src", "GPTDeskTop", "UI", "SimpleMonitorForm.cs");
        Assert.Contains("ConsecutivePassiveReadFailures", runner, StringComparison.Ordinal);
        Assert.Contains("LastRecovery", runner, StringComparison.Ordinal);
        Assert.Contains("LastTransientError", runner, StringComparison.Ordinal);
        Assert.Contains("Total CDP retries", form, StringComparison.Ordinal);
        Assert.Contains("Consecutive:", form, StringComparison.Ordinal);
        Assert.Contains("Last transient (recovered)", form, StringComparison.Ordinal);
    }
}
''',
    encoding="utf-8",
    newline="\n",
)

print("Patch applied successfully.")

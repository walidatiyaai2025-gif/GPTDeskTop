from pathlib import Path

path = Path("scripts/v2033_hardening_patch.py")
text = path.read_text(encoding="utf-8")


def remove_between(start_marker: str, end_marker: str) -> None:
    global text
    start = text.index(start_marker)
    end = text.index(end_marker, start)
    text = text[:start] + text[end:]


# Do not delete or replace legacy ChromeDevToolsService recovery/launcher behavior.
# Other product modes depend on it. Monitor Only gets an instance-level no-mutation policy.
remove_between(
    'replace_block(\n    chrome,\n    "    public Process LaunchMonitorChrome',
    'replace_block(\n    chrome,\n    "    private async Task<bool> RecoverMonitorTabAsync',
)
remove_between(
    'replace_block(\n    chrome,\n    "    private async Task<bool> RecoverMonitorTabAsync',
    'replace_block(\n    chrome,\n    "    private async Task<bool> RefreshStuckComposerAsync',
)
remove_between(
    'replace_block(\n    chrome,\n    "    private async Task<bool> RefreshStuckComposerAsync',
    'replace_once(\n    chrome,\n    \'    private async Task<JsonElement> EvaluateAsync',
)

# Add an instance-level recovery policy. Legacy callers keep the default true;
# SimpleMonitorProfileSession explicitly opts out of browser mutation recovery.
runner_marker = '\nrunner = "src/GPTDeskTop/Services/SimpleMonitorRunner.cs"\n'
extra = r'''
replace_once(
    chrome,
    "    private readonly ChromeDevToolsSessionPool _sessionPool = new();",
    "    private readonly ChromeDevToolsSessionPool _sessionPool = new();\n"
    "    private readonly bool _allowBrowserMutationRecovery;",
)
replace_once(
    chrome,
    "    public ChromeDevToolsService(HttpClient httpClient, ChromeConfig config) { _httpClient = httpClient; _config = config; }",
    "    public ChromeDevToolsService(HttpClient httpClient, ChromeConfig config, bool allowBrowserMutationRecovery = true) { _httpClient = httpClient; _config = config; _allowBrowserMutationRecovery = allowBrowserMutationRecovery; }",
)
replace_once(
    chrome,
    '''    private async Task<bool> RecoverMonitorTabAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            return false;

        await _monitorBrowserRecoveryGate.WaitAsync(cancellationToken);''',
    '''    private async Task<bool> RecoverMonitorTabAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            return false;

        if (!_allowBrowserMutationRecovery)
            return await TryPassiveRebindConversationAsync(tab, cancellationToken).ConfigureAwait(false);

        await _monitorBrowserRecoveryGate.WaitAsync(cancellationToken);''',
)
insert_marker = "    private async Task<bool> RefreshConversationTabAsync(ChromeTab conversationTab, CancellationToken cancellationToken)\n"
insert_text = '''    private async Task<bool> TryPassiveRebindConversationAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        _sessionPool.Invalidate(tab.Id);
        var replacement = await TryFindConversationTabAsync(tab.Url, cancellationToken).ConfigureAwait(false);
        if (replacement is null
            || !RuntimeHealthPresentation.IsChatGptConversationUrl(replacement.Url)
            || !ChatGptConversationIdentity.IsSame(tab.Url, replacement.Url))
            return false;

        RebindTab(tab, replacement);
        return true;
    }

'''
chrome_text = read(chrome)
if chrome_text.count(insert_marker) != 1:
    raise SystemExit("Could not locate RefreshConversationTabAsync insertion marker")
write(chrome, chrome_text.replace(insert_marker, insert_text + insert_marker, 1))
replace_once(
    chrome,
    '''    private async Task<bool> RefreshStuckComposerAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            return false;

        await _monitorBrowserRecoveryGate.WaitAsync(cancellationToken);''',
    '''    private async Task<bool> RefreshStuckComposerAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        if (!RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url))
            return false;

        if (!_allowBrowserMutationRecovery)
        {
            if (!await TryPassiveRebindConversationAsync(tab, cancellationToken).ConfigureAwait(false))
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

        await _monitorBrowserRecoveryGate.WaitAsync(cancellationToken);''',
)

session = "src/GPTDeskTop/Services/SimpleMonitorProfileSession.cs"
replace_once(
    session,
    '''            SmartAutoFollowThrottleMilliseconds = 400,
            SmartAutoFollowNearBottomPixels = 180
        });''',
    '''            SmartAutoFollowThrottleMilliseconds = 400,
            SmartAutoFollowNearBottomPixels = 180
        }, allowBrowserMutationRecovery: false);''',
)
'''
text = text.replace(runner_marker, extra + runner_marker, 1)

# Scope the QA gate to the Monitor Only instance policy rather than forbidding
# legacy Chrome recovery code globally.
global_gate_start = "          if ($chrome -match 'Process"
if global_gate_start in text:
    remove_between(global_gate_start, "          $recoverStart =")

recover_gate_start = "          $recoverStart ="
write_host_marker = "          Write-Host 'PASS: Start-only launch, passive timeout, passive recovery and diagnostics contracts are enforced.'"
if recover_gate_start in text:
    start = text.index(recover_gate_start)
    end = text.index(write_host_marker, start)
    replacement = r'''          if ($session -notmatch 'allowBrowserMutationRecovery:\s*false') {
            throw 'Monitor Only session is not explicitly disabling browser-mutating recovery.'
          }
          if ($chrome -notmatch 'if\s*\(!_allowBrowserMutationRecovery\)\s*return await TryPassiveRebindConversationAsync') {
            throw 'Monitor recovery does not switch to passive rebind for Monitor Only.'
          }
          if ($chrome -notmatch 'if\s*\(!_allowBrowserMutationRecovery\)[\s\S]*?ReadComposerReadinessAsync') {
            throw 'Stuck-composer recovery does not have a passive Monitor Only branch.'
          }

'''
    text = text[:start] + replacement + text[end:]

# Replace the generated global-recovery regression with an instance-policy test.
old_test_start = '    [Fact]\n    public void RecoveryCannotReloadCreateOrRelaunchBrowser()'
if old_test_start in text:
    start = text.index(old_test_start)
    next_fact = text.index('    [Fact]', start + len(old_test_start))
    new_test = '''    [Fact]\n    public void MonitorOnlyDisablesBrowserMutatingRecovery()\n    {\n        var session = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs");\n        var chrome = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");\n\n        Assert.Contains("allowBrowserMutationRecovery: false", session, StringComparison.Ordinal);\n        Assert.Contains("if (!_allowBrowserMutationRecovery)", chrome, StringComparison.Ordinal);\n        Assert.Contains("TryPassiveRebindConversationAsync", chrome, StringComparison.Ordinal);\n    }\n\n'''
    text = text[:start] + new_test + text[next_fact:]

path.write_text(text, encoding="utf-8", newline="\n")
print("Scoped v2.0.33 hardening to Monitor Only without weakening legacy recovery.")

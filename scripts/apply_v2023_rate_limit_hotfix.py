from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")


def write(rel, text):
    (ROOT / rel).write_text(text, encoding="utf-8")


def replace_once(rel, old, new):
    text = read(rel)
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{rel}: expected one occurrence, found {count}: {old[:120]!r}")
    write(rel, text.replace(old, new, 1))


def replace_all(rel, old, new, minimum=1):
    text = read(rel)
    count = text.count(old)
    if count < minimum:
        raise SystemExit(f"{rel}: expected at least {minimum} occurrence(s), found {count}: {old[:120]!r}")
    write(rel, text.replace(old, new))


runner = "src/GPTDeskTop/Services/SimpleMonitorRunner.cs"
replace_once(
    runner,
    "using System.Reflection;\nusing GPTDeskTop.Models;",
    "using System.Reflection;\nusing GPTDeskTop.Data;\nusing GPTDeskTop.Models;")
replace_once(
    runner,
    '    private string _lastError = string.Empty;\n\n    public event Action<string>? StatusChanged;',
    '''    private string _lastError = string.Empty;\n    private readonly SimpleMonitorSafetyGate _safety;\n\n    public SimpleMonitorRunner() : this(null) { }\n\n    public SimpleMonitorRunner(LocalDatabase? database)\n    {\n        _safety = new SimpleMonitorSafetyGate(database);\n    }\n\n    public event Action<string>? StatusChanged;''')

for name in ("initial", "before", "recheck"):
    replace_once(
        runner,
        f"            ThrowIfUnsafe({name});",
        f"            await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false);\n            ThrowIfUnsafe({name});")

replace_all(
    runner,
    "            ThrowIfUnsafe(state);",
    "            await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false);\n            ThrowIfUnsafe(state);",
    minimum=2)

replace_once(
    runner,
    '''                var runtimeMessage = messages[messageIndex];\n                var step = runtimeMessage.Step;''',
    '''                await using var sendPermit = await _safety.AcquireSendPermitAsync(\n                    session.Chrome,\n                    token => RequireSameConversationAsync(session, conversationUrl, token),\n                    (liveTab, token) => ReadPassiveStateResilientAsync(session.Chrome, liveTab, token),\n                    status => SetStatus(status, "SendGate"),\n                    cancellationToken).ConfigureAwait(false);\n                tab = sendPermit.Tab;\n                before = sendPermit.State;\n                ThrowIfUnsafe(before);\n\n                var runtimeMessage = messages[messageIndex];\n                var step = runtimeMessage.Step;''')

replace_once(
    runner,
    '''                var sent = await session.Chrome.SendChatMessageVerifiedAsync(\n                    tab,\n                    message,\n                    cancellationToken,\n                    requireNewTurn: true).ConfigureAwait(false);\n                if (!sent)\n                    throw new SimpleMonitorBlockedException("The exact stored message was not safely confirmed as sent. Automatic retry is blocked to prevent a duplicate.");''',
    '''                await sendPermit.RecordPhysicalAttemptAsync(CancellationToken.None).ConfigureAwait(false);\n                bool sent;\n                try\n                {\n                    sent = await SimpleMonitorPassiveReadGate.RunAsync(\n                        () => session.Chrome.SendChatMessageVerifiedAsync(\n                            tab,\n                            message,\n                            cancellationToken,\n                            requireNewTurn: true),\n                        cancellationToken).ConfigureAwait(false);\n                }\n                catch (Exception) when (!cancellationToken.IsCancellationRequested)\n                {\n                    if (await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false))\n                    {\n                        SetStatus("RATE LIMITED — physical submit was rejected. Safe backoff completed; retry remains behind the global send gate.", "RateLimited");\n                        continue;\n                    }\n                    throw;\n                }\n\n                if (!sent)\n                {\n                    if (await HandleRateLimitIfNeededAsync(session, conversationUrl, tab, cancellationToken).ConfigureAwait(false))\n                    {\n                        SetStatus("RATE LIMITED — physical submit was rejected. Safe backoff completed; retry remains behind the global send gate.", "RateLimited");\n                        continue;\n                    }\n                    throw new SimpleMonitorBlockedException("The exact stored message was not safely confirmed as sent. Automatic retry is blocked to prevent a duplicate.");\n                }''')

replace_once(
    runner,
    '''                await WaitForNewResponseCompletionAsync(\n                    session,\n                    conversationUrl,\n                    before,\n                    cancellationToken).ConfigureAwait(false);\n\n                var stepDelaySeconds''',
    '''                await WaitForNewResponseCompletionAsync(\n                    session,\n                    conversationUrl,\n                    before,\n                    cancellationToken).ConfigureAwait(false);\n                await _safety.RecordResponseCompletedAsync(CancellationToken.None).ConfigureAwait(false);\n\n                var stepDelaySeconds''')

text = read(runner)
pattern = re.compile(r'''    private static async Task<ChatPageState> InvokePassiveStateReaderAsync\(\n        ChromeDevToolsService chrome,\n        ChromeTab tab,\n        CancellationToken cancellationToken\)\n    \{.*?\n    \}\n\n    private static bool IsTransientRuntimeEvaluateTimeout''', re.S)
replacement = '''    private static Task<ChatPageState> InvokePassiveStateReaderAsync(\n        ChromeDevToolsService chrome,\n        ChromeTab tab,\n        CancellationToken cancellationToken)\n        => SimpleMonitorPassiveReadGate.RunAsync(async () =>\n        {\n            try\n            {\n                var task = (Task<ChatPageState>)(PassiveStateReader.Invoke(\n                    chrome,\n                    new object[] { tab, cancellationToken })\n                    ?? throw new InvalidOperationException("Passive chat-state reader returned no task."));\n                return await task.ConfigureAwait(false);\n            }\n            catch (TargetInvocationException ex) when (ex.InnerException is not null)\n            {\n                throw ex.InnerException;\n            }\n        }, cancellationToken);\n\n    private static bool IsTransientRuntimeEvaluateTimeout'''
text, count = pattern.subn(replacement, text, count=1)
if count != 1:
    raise SystemExit(f"{runner}: could not replace InvokePassiveStateReaderAsync")
write(runner, text)

replace_once(
    runner,
    '''    private static void ThrowIfUnsafe(ChatPageState state)\n    {''',
    '''    private async Task<bool> HandleRateLimitIfNeededAsync(\n        SimpleMonitorProfileSession session,\n        string conversationUrl,\n        ChromeTab tab,\n        CancellationToken cancellationToken)\n    {\n        var active = await _safety.ObserveRateLimitAsync(session.Chrome, tab, cancellationToken).ConfigureAwait(false);\n        if (!active) return false;\n\n        SetStatus("RATE LIMITED — ChatGPT temporarily limited this profile. All physical sends are globally paused.", "RateLimited");\n        await _safety.WaitForRateLimitClearAsync(\n            session.Chrome,\n            token => RequireSameConversationAsync(session, conversationUrl, token),\n            status => SetStatus(status, "RateLimited"),\n            cancellationToken).ConfigureAwait(false);\n        return true;\n    }\n\n    private static void ThrowIfUnsafe(ChatPageState state)\n    {''')

form = "src/GPTDeskTop/UI/SimpleMonitorForm.cs"
replace_once(form, "    private readonly SimpleMonitorRunner _runner = new();", "    private readonly SimpleMonitorRunner _runner;")
replace_once(
    form,
    "        _database = database ?? throw new ArgumentNullException(nameof(database));\n\n        Text = \"GPTDeskTop — Monitor Only\";",
    "        _database = database ?? throw new ArgumentNullException(nameof(database));\n        _runner = new SimpleMonitorRunner(_database);\n\n        Text = \"GPTDeskTop — Monitor Only\";")

controller = "src/GPTDeskTop/UI/MonitorOnlyExperienceController.cs"
replace_once(controller, "new() { Interval = 750 }", "new() { Interval = 1500 }")
text = read(controller)
pattern = re.compile(r'''    private static async Task<LiveChatSnapshot> ReadLiveSnapshotAsync\(\n        ChromeDevToolsService chrome,\n        ChromeTab tab,\n        CancellationToken cancellationToken\)\n    \{.*?\n    \}\n\n    private static string CompactTail''', re.S)
replacement = '''    private static Task<LiveChatSnapshot> ReadLiveSnapshotAsync(\n        ChromeDevToolsService chrome,\n        ChromeTab tab,\n        CancellationToken cancellationToken)\n        => SimpleMonitorPassiveReadGate.RunAsync(async () =>\n        {\n            try\n            {\n                var task = (Task<ChatPageState>)(PassiveStateReader.Invoke(\n                    chrome,\n                    new object[] { tab, cancellationToken })\n                    ?? throw new InvalidOperationException("Passive live chat reader returned no task."));\n                var state = await task.ConfigureAwait(true);\n                return new LiveChatSnapshot(state.IsGenerating, state.LastAssistantText ?? string.Empty);\n            }\n            catch (TargetInvocationException ex) when (ex.InnerException is not null)\n            {\n                throw ex.InnerException;\n            }\n        }, cancellationToken);\n\n    private static string CompactTail'''
text, count = pattern.subn(replacement, text, count=1)
if count != 1:
    raise SystemExit(f"{controller}: could not replace ReadLiveSnapshotAsync")
write(controller, text)

props = "Directory.Build.props"
replace_once(props, "<GPTDeskTopVersion>2.0.22</GPTDeskTopVersion>", "<GPTDeskTopVersion>2.0.23</GPTDeskTopVersion>")

simple_tests = "tests/GPTDeskTop.RuntimeTests/SimpleMonitorModeRegressionTests.cs"
replace_all(simple_tests, "Interval = 750", "Interval = 1500")
replace_all(simple_tests, "TwoPointZeroPointTwentyTwo", "TwoPointZeroPointTwentyThree")
replace_all(simple_tests, "<GPTDeskTopVersion>2.0.22</GPTDeskTopVersion>", "<GPTDeskTopVersion>2.0.23</GPTDeskTopVersion>")

visual_tests = "tests/GPTDeskTop.RuntimeTests/MonitorOnlyVisualHotfixRegressionTests.cs"
replace_all(visual_tests, "TwoPointZeroPointTwentyTwo", "TwoPointZeroPointTwentyThree")
replace_all(visual_tests, "<GPTDeskTopVersion>2.0.22</GPTDeskTopVersion>", "<GPTDeskTopVersion>2.0.23</GPTDeskTopVersion>")

print("v2.0.23 rate-limit safety patch applied")

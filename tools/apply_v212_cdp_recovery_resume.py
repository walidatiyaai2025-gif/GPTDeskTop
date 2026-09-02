from pathlib import Path

root = Path(__file__).resolve().parents[1]
monitor_path = root / 'src/GPTDeskTop/Services/ChatGptMonitorService.cs'
text = monitor_path.read_text(encoding='utf-8')

old = '    private static readonly TimeSpan MinimumStableSendDwell = TimeSpan.FromSeconds(15);\n'
new = old + '    private static readonly TimeSpan MonitorChatStateReadTimeout = TimeSpan.FromSeconds(12);\n'
if 'MonitorChatStateReadTimeout' not in text:
    if old not in text:
        raise SystemExit('minimum dwell marker missing')
    text = text.replace(old, new, 1)

old_poll = '                    var state = await _chrome.GetChatStateAsync(tab, cancellationToken);'
new_poll = '                    var state = await GetChatStateWithRetryAsync(monitor.Id, tab, cancellationToken);'
if old_poll not in text and new_poll not in text:
    raise SystemExit('poll state-read marker missing')
text = text.replace(old_poll, new_poll, 1)

start_marker = '    private async Task<ChatPageState> GetChatStateWithRetryAsync(long monitorId, ChromeTab tab, CancellationToken cancellationToken)\n'
end_marker = '    private static bool IsTransientChromeException(Exception ex)'
start = text.find(start_marker)
end = text.find(end_marker, start)
if start < 0 or end < 0:
    raise SystemExit('retry helper markers missing')
replacement = '''    private async Task<ChatPageState> GetChatStateWithRetryAsync(long monitorId, ChromeTab tab, CancellationToken cancellationToken)
    {
        var recoveredTransport = false;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var state = await ReadChatStateBoundedAsync(tab, cancellationToken);
                if (recoveredTransport)
                {
                    RuntimeFlightRecorder.Record(
                        "Monitor", "TransportRecoveryResume", "resumed", "bounded-authoritative-state-read",
                        monitorId, tab.Id, tab.Url);
                    Activity?.Invoke(monitorId, "Chrome/CDP recovery read verified; monitor polling resumed.");
                }
                return state;
            }
            catch (Exception ex) when (IsTransientChromeException(ex))
            {
                if (attempt <= 3 || attempt % 12 == 0)
                    Activity?.Invoke(monitorId, $"Chrome/CDP transport disconnect retry {attempt}: {ex.GetType().Name}. Rebinding the same conversation target.");

                var recovered = await _chrome.EnsureStableConversationTransportAsync(
                    tab,
                    cancellationToken,
                    stableReadsRequired: 3);
                if (recovered)
                {
                    recoveredTransport = true;
                    Activity?.Invoke(monitorId, "Chrome/CDP recovery complete: same conversation target is stable.");
                    attempt = 0;
                    await Task.Delay(150, cancellationToken);
                    continue;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5000, 500 * attempt)), cancellationToken);
            }
        }
    }

    private async Task<ChatPageState> ReadChatStateBoundedAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readCancellation.CancelAfter(MonitorChatStateReadTimeout);
        try
        {
            return await _chrome.GetChatStateAsync(tab, readCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && readCancellation.IsCancellationRequested)
        {
            throw new TimeoutException($"ChatGPT state read exceeded the {MonitorChatStateReadTimeout.TotalSeconds:0}-second monitor recovery deadline.");
        }
    }

'''
text = text[:start] + replacement + text[end:]
monitor_path.write_text(text, encoding='utf-8')

# Bump product/setup versions without rewriting historical documentation.
for relative in [
    'src/GPTDeskTop/GPTDeskTop.csproj',
    'src/GPTDeskTop.Build/GPTDeskTop.Build.csproj',
    'src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj',
    'src/GPTDeskTop.Setup/Program.cs',
]:
    path = root / relative
    value = path.read_text(encoding='utf-8')
    value = value.replace('2.0.11', '2.0.12')
    path.write_text(value, encoding='utf-8')

# Current-version assertions in the runtime test project must follow the release version.
for path in (root / 'tests/GPTDeskTop.RuntimeTests').glob('*.cs'):
    value = path.read_text(encoding='utf-8')
    if '2.0.11' in value:
        path.write_text(value.replace('2.0.11', '2.0.12'), encoding='utf-8')

regression = root / 'tests/GPTDeskTop.RuntimeTests/CdpRecoveryResumeRegressionTests.cs'
regression.write_text(r'''using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CdpRecoveryResumeRegressionTests
{
    [Fact]
    public void MonitorPollUsesRecoveryAwareStateReadInsteadOfRawCdpRead()
    {
        var source = MonitorSource();
        var loop = Slice(source, "private async Task MonitorLoopAsync", "private async Task<bool> ConfirmFreshChatGenerationBoundaryAsync");
        Assert.Contains("var state = await GetChatStateWithRetryAsync(monitor.Id, tab, cancellationToken);", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("var state = await _chrome.GetChatStateAsync(tab, cancellationToken);", loop, StringComparison.Ordinal);
    }

    [Fact]
    public void StateReadIsBoundedSoRecoveredTransportCannotHangTheMonitorForever()
    {
        var source = MonitorSource();
        Assert.Contains("MonitorChatStateReadTimeout = TimeSpan.FromSeconds(12)", source, StringComparison.Ordinal);
        var bounded = Slice(source, "private async Task<ChatPageState> ReadChatStateBoundedAsync", "private static bool IsTransientChromeException");
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)", bounded, StringComparison.Ordinal);
        Assert.Contains("CancelAfter(MonitorChatStateReadTimeout)", bounded, StringComparison.Ordinal);
        Assert.Contains("throw new TimeoutException", bounded, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulRebindRequiresAnAuthoritativeReadBeforeRecoveryIsDeclaredResumed()
    {
        var source = MonitorSource();
        var retry = Slice(source, "private async Task<ChatPageState> GetChatStateWithRetryAsync", "private async Task<ChatPageState> ReadChatStateBoundedAsync");
        var recovery = retry.IndexOf("Chrome/CDP recovery complete: same conversation target is stable.", StringComparison.Ordinal);
        var boundedRead = retry.IndexOf("await ReadChatStateBoundedAsync(tab, cancellationToken)", StringComparison.Ordinal);
        var resumed = retry.IndexOf("Chrome/CDP recovery read verified; monitor polling resumed.", StringComparison.Ordinal);
        Assert.True(recovery >= 0 && boundedRead >= 0 && resumed >= 0);
        Assert.Contains("recoveredTransport = true", retry, StringComparison.Ordinal);
        Assert.Contains("TransportRecoveryResume", retry, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryFixDoesNotRelaxExactlyOnceOrGlobalDwellInvariants()
    {
        var source = MonitorSource();
        Assert.Contains("MinimumStableSendDwell = TimeSpan.FromSeconds(15)", source, StringComparison.Ordinal);
        Assert.Contains("Verified message accepted. Exactly-once guard closed the delivery operation.", source, StringComparison.Ordinal);
        Assert.Contains("Exactly-once guard suppressed blind resend", source, StringComparison.Ordinal);
        Assert.Contains("ContinueInFreshChatAfterResponseAsync", source, StringComparison.Ordinal);
    }

    private static string MonitorSource() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChatGptMonitorService.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
''', encoding='utf-8')

print('v2.0.12 CDP recovery resume patch applied')

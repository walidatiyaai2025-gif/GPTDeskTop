from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly 1 match, found {count}: {old!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")

replace_once("Directory.Build.props", "<GPTDeskTopVersion>2.0.34</GPTDeskTopVersion>", "<GPTDeskTopVersion>2.0.35</GPTDeskTopVersion>")

safety = "src/GPTDeskTop/Services/SimpleMonitorSafetyGate.cs"
replace_once(safety,
    "internal static readonly TimeSpan MinimumSendGap = TimeSpan.FromSeconds(15);",
    "internal static readonly TimeSpan MinimumSendGap = TimeSpan.FromSeconds(30);\n    internal const int MicroBreakEveryConfirmedMessages = 25;\n    internal static readonly TimeSpan MicroBreakDuration = TimeSpan.FromMinutes(2);")
replace_once(safety,
    "status?.Invoke(\"RATE LIMIT CLEARED — safe probe passed. Normal 15-second send gate remains enforced.\");",
    "status?.Invoke(\"RATE LIMIT CLEARED — safe probe passed. Normal 30-second send gate remains enforced.\");")
replace_once(safety,
'''        while (true)
        {
            DateTimeOffset notBefore;
            lock (_sync)
            {
                notBefore = _startupQuietUntilUtc;
                if (_state.LastPhysicalAttemptUtc is { } physical)
                    notBefore = Max(notBefore, physical + MinimumSendGap);
                if (_state.LastResponseCompletedUtc is { } completed)
                    notBefore = Max(notBefore, completed + MinimumSendGap);
            }

            var remaining = notBefore - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return;

            status?.Invoke($"SEND GATE — safety quiet period {FormatRemaining(remaining)}. No physical send yet.");
            await Task.Delay(remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining, cancellationToken).ConfigureAwait(false);
        }
''',
'''        while (true)
        {
            DateTimeOffset notBefore;
            DateTimeOffset? microBreakUntil;
            lock (_sync)
            {
                notBefore = _startupQuietUntilUtc;
                if (_state.LastPhysicalAttemptUtc is { } physical)
                    notBefore = Max(notBefore, physical + MinimumSendGap);
                if (_state.LastResponseCompletedUtc is { } completed)
                    notBefore = Max(notBefore, completed + MinimumSendGap);
                microBreakUntil = _state.MicroBreakUntilUtc;
                if (microBreakUntil is { } pause)
                    notBefore = Max(notBefore, pause);
            }

            var remaining = notBefore - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return;

            var microBreakActive = microBreakUntil is { } breakUntil && breakUntil > DateTimeOffset.UtcNow;
            status?.Invoke(microBreakActive
                ? $"SEND GATE — scheduled 2-minute micro-break after {MicroBreakEveryConfirmedMessages} confirmed messages; {FormatRemaining(remaining)} remaining. No physical send yet."
                : $"SEND GATE — safety quiet period {FormatRemaining(remaining)}. No physical send yet.");
            await Task.Delay(remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining, cancellationToken).ConfigureAwait(false);
        }
''')
replace_once(safety,
'''    internal async Task RecordResponseCompletedAsync(CancellationToken cancellationToken)
    {
''',
'''    internal async Task RecordConfirmedDeliveryAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        DurableState next;
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var count = Math.Max(0, _state.ConfirmedSendsSinceMicroBreak) + 1;
            var microBreakUntil = _state.MicroBreakUntilUtc is { } existing && existing > now ? existing : null;
            if (count >= MicroBreakEveryConfirmedMessages)
            {
                count = 0;
                microBreakUntil = now + MicroBreakDuration;
            }

            next = _state with
            {
                ConfirmedSendsSinceMicroBreak = count,
                MicroBreakUntilUtc = microBreakUntil
            };
            _state = next;
        }
        await PersistAsync(next, CancellationToken.None).ConfigureAwait(false);
    }

    internal async Task RecordResponseCompletedAsync(CancellationToken cancellationToken)
    {
''')
replace_once(safety,
'''    private sealed record DurableState(
        DateTimeOffset? LastPhysicalAttemptUtc,
        DateTimeOffset? LastResponseCompletedUtc,
        bool RateLimitActive,
        int BackoffIndex,
        DateTimeOffset? RetryAtUtc,
        DateTimeOffset? DetectedAtUtc,
        string LastRateLimitText)
    {
        internal static DurableState Empty { get; } = new(null, null, false, 0, null, null, string.Empty);
    }
''',
'''    private sealed record DurableState(
        DateTimeOffset? LastPhysicalAttemptUtc,
        DateTimeOffset? LastResponseCompletedUtc,
        bool RateLimitActive,
        int BackoffIndex,
        DateTimeOffset? RetryAtUtc,
        DateTimeOffset? DetectedAtUtc,
        string LastRateLimitText,
        int ConfirmedSendsSinceMicroBreak,
        DateTimeOffset? MicroBreakUntilUtc)
    {
        internal static DurableState Empty { get; } = new(null, null, false, 0, null, null, string.Empty, 0, null);
    }
''')

runner = "src/GPTDeskTop/Services/SimpleMonitorRunner.cs"
replace_once(runner, "Math.Clamp(defaultDelaySeconds, 15, 3600)", "Math.Clamp(defaultDelaySeconds, 30, 3600)")
replace_once(runner, "global 15-second gate", "global 30-second gate")
replace_once(runner,
'''                if (checkpoint is not null)
                    await checkpoint(runtimeMessage.OriginalIndex, _totalMessages, message, cancellationToken).ConfigureAwait(false);
                _sentMessages++;
''',
'''                if (checkpoint is not null)
                    await checkpoint(runtimeMessage.OriginalIndex, _totalMessages, message, cancellationToken).ConfigureAwait(false);
                await _safety.RecordConfirmedDeliveryAsync(CancellationToken.None).ConfigureAwait(false);
                _sentMessages++;
''')

replace_once("src/GPTDeskTop/UI/SimpleMonitorForm.cs",
    "new() { Minimum = 15, Maximum = 3600, Value = 15, Width = 90 }",
    "new() { Minimum = 30, Maximum = 3600, Value = 30, Width = 90 }")

for test in [
    "tests/GPTDeskTop.RuntimeTests/SimpleMonitorRateLimitOverlayRegressionTests.cs",
    "tests/GPTDeskTop.RuntimeTests/SimpleMonitorModeRegressionTests.cs",
    "tests/GPTDeskTop.RuntimeTests/MonitorOnlyVisualHotfixRegressionTests.cs",
]:
    p = Path(test)
    text = p.read_text(encoding="utf-8")
    text = text.replace("2.0.34", "2.0.35")
    text = text.replace("ThirtyFour", "ThirtyFive")
    text = text.replace("Minimum = 15", "Minimum = 30")
    p.write_text(text, encoding="utf-8")

Path("tests/GPTDeskTop.RuntimeTests/SimpleMonitorPacingRegressionTests.cs").write_text(r'''using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorPacingRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public async Task TwentyFiveConfirmedDeliveriesArmADurableTwoMinuteMicroBreak()
    {
        var assembly = typeof(SimpleMonitorRunner).Assembly;
        var gateType = assembly.GetType("GPTDeskTop.Services.SimpleMonitorSafetyGate", throwOnError: true)!;
        var constructor = gateType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single(ctor => ctor.GetParameters().Length == 1);
        var gate = constructor.Invoke(new object?[] { null });
        var recordConfirmed = gateType.GetMethod("RecordConfirmedDeliveryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        for (var i = 0; i < 25; i++)
            await (Task)recordConfirmed.Invoke(gate, new object?[] { CancellationToken.None })!;

        var state = gateType.GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gate)!;
        var stateType = state.GetType();
        Assert.Equal(0, (int)stateType.GetProperty("ConfirmedSendsSinceMicroBreak")!.GetValue(state)!);
        var breakUntil = (DateTimeOffset?)stateType.GetProperty("MicroBreakUntilUtc")!.GetValue(state);
        Assert.NotNull(breakUntil);
        Assert.True(breakUntil > DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1));

        var minimumGap = (TimeSpan)gateType.GetField("MinimumSendGap", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.Equal(TimeSpan.FromSeconds(30), minimumGap);
    }

    [Fact]
    public void ConfirmedDeliveryIsCountedOnlyAfterDurableCheckpoint()
    {
        var runner = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorRunner.cs");
        var checkpoint = runner.IndexOf("await checkpoint(runtimeMessage.OriginalIndex", StringComparison.Ordinal);
        var pacing = runner.IndexOf("await _safety.RecordConfirmedDeliveryAsync", StringComparison.Ordinal);
        var sentProgress = runner.IndexOf("_sentMessages++;", StringComparison.Ordinal);
        Assert.True(checkpoint >= 0 && pacing > checkpoint && sentProgress > pacing);
        Assert.Contains("Math.Clamp(defaultDelaySeconds, 30, 3600)", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void PacingStateUsesTheExistingDurableSafetyState()
    {
        var safety = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs");
        Assert.Contains("MicroBreakEveryConfirmedMessages = 25", safety, StringComparison.Ordinal);
        Assert.Contains("MicroBreakDuration = TimeSpan.FromMinutes(2)", safety, StringComparison.Ordinal);
        Assert.Contains("ConfirmedSendsSinceMicroBreak", safety, StringComparison.Ordinal);
        Assert.Contains("MicroBreakUntilUtc", safety, StringComparison.Ordinal);
        Assert.Contains("PersistAsync(next, CancellationToken.None)", safety, StringComparison.Ordinal);
    }
}
''', encoding="utf-8")

print("v2.0.35 pacing patch applied")

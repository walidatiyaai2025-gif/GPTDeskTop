using System.Reflection;
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

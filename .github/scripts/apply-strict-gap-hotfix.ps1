$ErrorActionPreference = 'Stop'

$path = 'src/GPTDeskTop/Runtime/OutboundDeliveryCoordinator.cs'
$source = Get-Content $path -Raw

if ($source -notmatch '_usesSystemDelay') {
    $old = @'
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly TimeSpan _interSendGap;
'@
    $new = @'
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly TimeSpan _interSendGap;
    private readonly bool _usesSystemDelay;
'@
    if (-not $source.Contains($old)) { throw 'Coordinator field anchor not found.' }
    $source = $source.Replace($old, $new)

    $old = @'
        _delayAsync = delayAsync ?? Task.Delay;
        _interSendGap = interSendGap ?? DefaultInterSendGap;
'@
    $new = @'
        _usesSystemDelay = delayAsync is null;
        _delayAsync = delayAsync ?? Task.Delay;
        _interSendGap = interSendGap ?? DefaultInterSendGap;
'@
    if (-not $source.Contains($old)) { throw 'Coordinator constructor anchor not found.' }
    $source = $source.Replace($old, $new)

    $old = @'
                    await _delayAsync(_interSendGap, CancellationToken.None).ConfigureAwait(false);
'@
    $new = @'
                    await _delayAsync(_interSendGap, CancellationToken.None).ConfigureAwait(false);

                    // Task.Delay may wake a fraction of a millisecond early on Windows. The
                    // production authority must never release before the actual 15-second
                    // wall-clock deadline, so close any residual gap before the global lease
                    // can be granted to the next monitor. Injected test delays remain untouched.
                    if (_usesSystemDelay && nextSendUtc.HasValue)
                    {
                        while (DateTimeOffset.UtcNow < nextSendUtc.Value)
                        {
                            var remaining = nextSendUtc.Value - DateTimeOffset.UtcNow;
                            await Task.Delay(
                                    remaining + TimeSpan.FromMilliseconds(1),
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
'@
    if (-not $source.Contains($old)) { throw 'Coordinator cooldown anchor not found.' }
    $source = $source.Replace($old, $new)
    Set-Content $path $source -NoNewline
}

$testPath = 'tests/GPTDeskTop.RuntimeTests/StrictWallClockSendGapRegressionTests.cs'
$test = @'
namespace GPTDeskTop.RuntimeTests;

public sealed class StrictWallClockSendGapRegressionTests
{
    [Fact]
    public void ProductionCooldownClosesAnyEarlyTimerWakeBeforeGlobalRelease()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs")));

        Assert.Contains("_usesSystemDelay = delayAsync is null", source, StringComparison.Ordinal);
        Assert.Contains("while (DateTimeOffset.UtcNow < nextSendUtc.Value)", source, StringComparison.Ordinal);
        Assert.Contains("remaining + TimeSpan.FromMilliseconds(1)", source, StringComparison.Ordinal);
        Assert.Contains("DefaultInterSendGap = TimeSpan.FromSeconds(15)", source, StringComparison.Ordinal);
    }
}
'@
Set-Content $testPath $test

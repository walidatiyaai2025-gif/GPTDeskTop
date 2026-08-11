using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class ChromeDevToolsLongRunningStabilityRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void CdpCommandPathHasIndependentBoundedTimeoutAndPreservesCallerCancellation()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("DevToolsCommandTimeout = TimeSpan.FromSeconds(12)", source, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("commandCts.CancelAfter(DevToolsCommandTimeout)", source, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)", source, StringComparison.Ordinal);
        Assert.Contains("throw new TimeoutException", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CdpReceiveBufferIsPooledAndMessageGrowthIsBounded()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("ArrayPool<byte>.Shared.Rent(ReceiveBufferSize)", source, StringComparison.Ordinal);
        Assert.Contains("ArrayPool<byte>.Shared.Return(buffer)", source, StringComparison.Ordinal);
        Assert.Contains("MaxDevToolsMessageBytes = 2 * 1024 * 1024", source, StringComparison.Ordinal);
        Assert.Contains("stream.Length + result.Count > MaxDevToolsMessageBytes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new byte[64 * 1024]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CdpCommandTimeoutRemainsARecoverableMonitorFailure()
    {
        var method = typeof(ChatGptMonitorService).GetMethod(
            "IsTransientChromeException",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(null, [new TimeoutException("CDP command timed out")])!);
    }
}

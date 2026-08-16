namespace GPTDeskTop.RuntimeTests;

public sealed class CdpSessionGateStallRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void SessionGateWaitIsBoundedByTheCdpCommandTimeout()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs"));

        Assert.Contains(
            "_commandGate.WaitAsync(CommandTimeout, cancellationToken)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_commandGate.WaitAsync(cancellationToken);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "timed out waiting for the session gate",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GateTimeoutBreaksTheWedgedSessionBeforeReturningFailure()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs"));

        var boundedWait = source.IndexOf(
            "_commandGate.WaitAsync(CommandTimeout, cancellationToken)",
            StringComparison.Ordinal);
        var markBroken = source.IndexOf("MarkBroken();", boundedWait, StringComparison.Ordinal);
        var timeout = source.IndexOf(
            "timed out waiting for the session gate",
            markBroken,
            StringComparison.Ordinal);
        var commandBody = source.IndexOf("using var commandCts", boundedWait, StringComparison.Ordinal);

        Assert.True(boundedWait >= 0);
        Assert.True(markBroken > boundedWait);
        Assert.True(timeout > markBroken);
        Assert.True(commandBody > timeout);
    }
}

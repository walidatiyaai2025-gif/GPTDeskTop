namespace GPTDeskTop.RuntimeTests;

public sealed class CdpSessionGateStallRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void SessionGateWaitIsBoundedByTheConfiguredCdpTimeout()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Services", "ChromeDevToolsSessionPool.cs"));

        Assert.Contains(
            "CommandTimeout = TimeSpan.FromSeconds(12)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "PassiveRuntimeEvaluateTimeout = TimeSpan.FromSeconds(30)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "commandTimeout ?? CommandTimeout",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_commandGate.WaitAsync(commandTimeout, cancellationToken)",
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
            "_commandGate.WaitAsync(commandTimeout, cancellationToken)",
            StringComparison.Ordinal);
        var markBroken = boundedWait >= 0
            ? source.IndexOf("MarkBroken();", boundedWait, StringComparison.Ordinal)
            : -1;
        var timeout = markBroken >= 0
            ? source.IndexOf("timed out waiting for the session gate", markBroken, StringComparison.Ordinal)
            : -1;
        var commandBody = timeout >= 0
            ? source.IndexOf("using var commandCts", timeout, StringComparison.Ordinal)
            : -1;

        Assert.True(boundedWait >= 0);
        Assert.True(markBroken > boundedWait);
        Assert.True(timeout > markBroken);
        Assert.True(commandBody > timeout);
    }
}

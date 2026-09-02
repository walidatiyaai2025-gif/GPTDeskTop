using System.Reflection;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class RenderedUserTurnReceiptRegressionTests
{
    [Fact]
    public void MarkdownRenderedUserTurnMatchesRawComposerSource()
    {
        const string expected = """
        YOU ARE FCC CODE DESKTOP — P00 WORKER.

        REPOSITORY:
        `https://github.com/example/repo`

        * first item
        * second item
        """;
        const string observed = """
        YOU ARE FCC CODE DESKTOP — P00 WORKER.

        REPOSITORY:
        https://github.com/example/repo

        first item
        second item
        """;

        Assert.True(Matches(observed, expected));
    }

    [Fact]
    public void LargeCollapsedRenderedTurnUsesStrongNormalizedPrefixEvidence()
    {
        var expected = string.Join(" ", Enumerable.Range(0, 220).Select(i => $"operation-{i:D3}-verification"));
        var observed = expected[..Math.Min(expected.Length, 420)];
        Assert.True(Matches(observed, expected));
    }

    [Fact]
    public void UnrelatedNewUserTurnNeverMatches()
    {
        var expected = string.Join(" ", Enumerable.Range(0, 220).Select(i => $"operation-{i:D3}-verification"));
        var observed = string.Join(" ", Enumerable.Range(0, 220).Select(i => $"different-{i:D3}-manual"));
        Assert.False(Matches(observed, expected));
    }

    [Fact]
    public void ImmediateObservationSamplesGenerationBeforeCallingRenderedMismatchAmbiguous()
    {
        var source = ChromeSource();
        var method = Slice(source, "private async Task<ImmediatePhysicalSubmitObservation> ObserveImmediatePhysicalSubmitAsync", "private async Task<bool> TryDispatchNativeSendClickAsync");
        var mismatch = method.IndexOf("observedDifferentUserTurn = true", StringComparison.Ordinal);
        var generation = method.IndexOf("if (readiness.IsGenerating)", StringComparison.Ordinal);
        var finalAmbiguous = method.LastIndexOf("if (observedDifferentUserTurn)", StringComparison.Ordinal);
        Assert.True(mismatch >= 0 && generation > mismatch && finalAmbiguous > generation);
    }

    [Fact]
    public void ReconciliationAcceptsGenerationWithANewRenderedUserTurnBeforeConflictClassification()
    {
        var source = ChromeSource();
        var method = Slice(source, "private async Task<UnacknowledgedSubmitReconciliationResult> ReconcileUnacknowledgedSubmitAsync", "private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync");
        var generationReceipt = method.IndexOf("generation-with-new-rendered-user-turn", StringComparison.Ordinal);
        var classification = method.IndexOf("ClassifyPostRefreshUserTurn", StringComparison.Ordinal);
        Assert.True(generationReceipt >= 0 && classification > generationReceipt);
    }

    private static bool Matches(string observed, string expected)
    {
        var type = typeof(ChatGptMonitorService).Assembly.GetType("GPTDeskTop.Services.MonitorDeliveryRecoveryPolicy", throwOnError: true)!;
        var method = type.GetMethod("IsMatchingUserTurnEvidence", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, "IsMatchingUserTurnEvidence");
        return Assert.IsType<bool>(method.Invoke(null, new object?[] { observed, expected }));
    }

    private static string ChromeSource() => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs")));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}

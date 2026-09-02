from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path, old, new):
    p = ROOT / path
    text = p.read_text(encoding='utf-8')
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected one match, found {count}: {old[:100]!r}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')


def replace_all(path, old, new, minimum=1):
    p = ROOT / path
    text = p.read_text(encoding='utf-8')
    if old not in text:
        if new in text:
            return
        raise RuntimeError(f'{path}: missing {old!r}')
    if text.count(old) < minimum:
        raise RuntimeError(f'{path}: insufficient matches for {old!r}')
    p.write_text(text.replace(old, new), encoding='utf-8')

policy = 'src/GPTDeskTop/Services/MonitorDeliveryRecoveryPolicy.cs'
chrome = 'src/GPTDeskTop/Services/ChromeDevToolsService.cs'

replace_once(
    policy,
    'internal static class MonitorDeliveryRecoveryPolicy\n{\n',
    '''internal static class MonitorDeliveryRecoveryPolicy\n{\n    internal static bool IsMatchingUserTurnEvidence(string? observedText, string? expectedText)\n    {\n        if (string.Equals(observedText?.Trim(), expectedText?.Trim(), StringComparison.Ordinal))\n            return true;\n\n        var observed = NormalizeUserTurnEvidence(observedText);\n        var expected = NormalizeUserTurnEvidence(expectedText);\n        if (observed.Length == 0 || expected.Length == 0)\n            return false;\n        if (string.Equals(observed, expected, StringComparison.Ordinal))\n            return true;\n\n        const int collapsedPrefixEvidenceLength = 256;\n        return expected.Length >= 512\n               && observed.Length >= collapsedPrefixEvidenceLength\n               && expected.AsSpan(0, collapsedPrefixEvidenceLength)\n                   .SequenceEqual(observed.AsSpan(0, collapsedPrefixEvidenceLength));\n    }\n\n    private static string NormalizeUserTurnEvidence(string? text)\n    {\n        if (string.IsNullOrWhiteSpace(text))\n            return string.Empty;\n\n        var source = text.Replace("\\r\\n", "\\n", StringComparison.Ordinal).Replace('\\r', '\\n');\n        var builder = new System.Text.StringBuilder(source.Length);\n        var pendingSpace = false;\n        foreach (var ch in source)\n        {\n            if (char.IsLetterOrDigit(ch))\n            {\n                if (pendingSpace && builder.Length > 0) builder.Append(' ');\n                builder.Append(char.ToLowerInvariant(ch));\n                pendingSpace = false;\n            }\n            else\n            {\n                pendingSpace = builder.Length > 0;\n            }\n        }\n\n        return builder.ToString().Trim();\n    }\n\n''')

replace_once(
    policy,
    '        if (string.Equals(observedLastText, expectedText, StringComparison.Ordinal))\n            return PostRefreshUserTurnObservation.ReceiptConfirmed;\n',
    '        if (IsMatchingUserTurnEvidence(observedLastText, expectedText))\n            return PostRefreshUserTurnObservation.ReceiptConfirmed;\n')

replace_all(chrome, 'string.Equals(before.LastText, expected, StringComparison.Ordinal)', 'MonitorDeliveryRecoveryPolicy.IsMatchingUserTurnEvidence(before.LastText, expected)')
replace_all(chrome, 'ComposerEvidenceTextEquals(current.LastText, expected)', 'MonitorDeliveryRecoveryPolicy.IsMatchingUserTurnEvidence(current.LastText, expected)')
replace_all(chrome, 'ComposerEvidenceTextEquals(snapshot.LastText, expected)', 'MonitorDeliveryRecoveryPolicy.IsMatchingUserTurnEvidence(snapshot.LastText, expected)')
replace_all(chrome, 'string.Equals(receiptBeforeRefresh.LastText, expected, StringComparison.Ordinal)', 'MonitorDeliveryRecoveryPolicy.IsMatchingUserTurnEvidence(receiptBeforeRefresh.LastText, expected)')
replace_all(chrome, 'string.Equals(receiptAfterRefresh.LastText, expected, StringComparison.Ordinal)', 'MonitorDeliveryRecoveryPolicy.IsMatchingUserTurnEvidence(receiptAfterRefresh.LastText, expected)')

replace_once(
    chrome,
    '''        var observationDeadline = DateTimeOffset.UtcNow.AddSeconds(4);\n        var stableStillReadyReads = 0;\n        var stableEmptyComposerReads = 0;\n''',
    '''        var observationDeadline = DateTimeOffset.UtcNow.AddSeconds(4);\n        var stableStillReadyReads = 0;\n        var stableEmptyComposerReads = 0;\n        var observedDifferentUserTurn = false;\n''')
replace_once(
    chrome,
    '''            if (snapshot.Success && snapshot.Count > baselineUserTurnCount)\n            {\n                if (MonitorDeliveryRecoveryPolicy.IsMatchingUserTurnEvidence(snapshot.LastText, expected))\n                    return ImmediatePhysicalSubmitObservation.ReceiptConfirmed;\n                return ImmediatePhysicalSubmitObservation.Ambiguous;\n            }\n\n            var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);\n''',
    '''            if (snapshot.Success && snapshot.Count > baselineUserTurnCount)\n            {\n                if (MonitorDeliveryRecoveryPolicy.IsMatchingUserTurnEvidence(snapshot.LastText, expected))\n                    return ImmediatePhysicalSubmitObservation.ReceiptConfirmed;\n                observedDifferentUserTurn = true;\n            }\n\n            var readiness = await ReadComposerReadinessAsync(tab, cancellationToken);\n''')
replace_once(
    chrome,
    '''        return stableStillReadyReads > 0 || stableEmptyComposerReads > 0\n            ? ImmediatePhysicalSubmitObservation.ClickNotAccepted\n            : ImmediatePhysicalSubmitObservation.Ambiguous;\n''',
    '''        if (observedDifferentUserTurn)\n            return ImmediatePhysicalSubmitObservation.Ambiguous;\n\n        return stableStillReadyReads > 0 || stableEmptyComposerReads > 0\n            ? ImmediatePhysicalSubmitObservation.ClickNotAccepted\n            : ImmediatePhysicalSubmitObservation.Ambiguous;\n''')

replace_once(
    chrome,
    '''                var composerReadiness = await ReadComposerReadinessAsync(tab, cancellationToken);\n                var composer = await ReadComposerTextAsync(tab, cancellationToken);\n                if (!composerReadiness.IsGenerating\n''',
    '''                var composerReadiness = await ReadComposerReadinessAsync(tab, cancellationToken);\n                if (receiptAfterRefresh.Success\n                    && receiptAfterRefresh.Count > baselineUserTurnCount\n                    && composerReadiness.IsGenerating)\n                {\n                    VerifiedSendDiagnostics.Record("ReceiptConfirmed", "generation-with-new-rendered-user-turn", 0);\n                    return UnacknowledgedSubmitReconciliationResult.ReceiptConfirmed;\n                }\n\n                var composer = await ReadComposerTextAsync(tab, cancellationToken);\n                if (!composerReadiness.IsGenerating\n''')

for path in [
    'src/GPTDeskTop/GPTDeskTop.csproj',
    'src/GPTDeskTop.Setup/GPTDeskTop.Setup.csproj',
    'src/GPTDeskTop.Build/GPTDeskTop.Build.csproj',
    'src/GPTDeskTop.Setup/Program.cs',
]:
    replace_all(path, '2.0.10', '2.0.11')

test = ROOT / 'tests/GPTDeskTop.RuntimeTests/RenderedUserTurnReceiptRegressionTests.cs'
test.write_text(r'''using System.Reflection;
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
''', encoding='utf-8')

print('v2.0.11 rendered user-turn receipt fix applied')

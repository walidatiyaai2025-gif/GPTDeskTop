using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class VerifiedSendDiagnosticsRegressionTests
{
    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }

    [Fact]
    public void RuntimeSnapshotContainsOnlySanitizedVerifiedSendState()
    {
        var names = typeof(VerifiedSendDiagnosticSnapshot)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(new[] { "Phase", "Reason", "SubmitAttempts", "ObservedAtUtc" }, names);
        Assert.DoesNotContain(names, name => name.Contains("Message", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Prompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Text", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Content", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Url", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Title", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Target", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeInspectorSurfacesVerifiedSendPhaseReasonAttemptsAndTimestamp()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");

        Assert.Contains("VerifiedSendDiagnostics.Last", source, StringComparison.Ordinal);
        Assert.Contains("internal sealed record RuntimeInspectorVerifiedSendDiagnostics", source, StringComparison.Ordinal);
        Assert.Contains("string Phase", source, StringComparison.Ordinal);
        Assert.Contains("string Reason", source, StringComparison.Ordinal);
        Assert.Contains("int SubmitAttempts", source, StringComparison.Ordinal);
        Assert.Contains("DateTimeOffset ObservedAtUtc", source, StringComparison.Ordinal);
        Assert.Contains("Verified send:", source, StringComparison.Ordinal);

        var recordStart = source.IndexOf("internal sealed record RuntimeInspectorVerifiedSendDiagnostics", StringComparison.Ordinal);
        var recordEnd = source.IndexOf("internal sealed record RuntimeInspectorUiOverflow", recordStart, StringComparison.Ordinal);
        Assert.True(recordStart >= 0 && recordEnd > recordStart);
        var record = source[recordStart..recordEnd];
        Assert.DoesNotContain("Message", record, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Prompt", record, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Text", record, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content", record, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Url", record, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifiedSendStateMachineRecordsReceiptReconciliationAndFailClosedOutcomes()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs");
        var start = source.IndexOf("public async Task<bool> SendChatMessageVerifiedAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task<(bool Success, int Count, string LastText)> TryGetUserMessageSnapshotAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("VerifiedSendDiagnostics.Record(\"AwaitingReceipt\"", method, StringComparison.Ordinal);
        Assert.Contains("VerifiedSendDiagnostics.Record(\"Reconciling\"", method, StringComparison.Ordinal);
        Assert.Contains("VerifiedSendDiagnostics.Record(\"RetryAuthorized\"", method, StringComparison.Ordinal);
        Assert.Contains("VerifiedSendDiagnostics.Record(\"ReceiptConfirmed\"", method, StringComparison.Ordinal);
        Assert.Contains("VerifiedSendDiagnostics.Record(\"FailedClosed\"", method, StringComparison.Ordinal);
    }
}

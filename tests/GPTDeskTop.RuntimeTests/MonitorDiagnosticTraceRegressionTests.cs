using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorDiagnosticTraceRegressionTests
{
    [Fact]
    public void HistoryRecordCarriesOnlyOperationalMetadata()
    {
        var log = new MessageLog
        {
            Id = 91,
            Timestamp = new DateTime(2026, 8, 13, 20, 30, 0, DateTimeKind.Utc),
            MonitorId = 7,
            Direction = "Outbound",
            Status = "Sent"
        };

        var record = MonitorDiagnosticTraceService.CreateHistoryRecord(log);

        Assert.Equal(91, record.HistoryLogId);
        Assert.Equal(7, record.MonitorId);
        Assert.Equal("Outbound", record.Direction);
        Assert.Equal("Sent", record.Status);
    }

    [Fact]
    public void StateRecordCarriesGenerationFlagsInsteadOfResponseBody()
    {
        var pageState = new ChatPageState(14, "assistant body", true, "rendered error body");
        var record = MonitorDiagnosticTraceService.CreateStateRecord(12, true, true, true, pageState);

        Assert.Equal(14, record.AssistantCount);
        Assert.True(record.IsGenerating);
        Assert.True(record.HasAssistantText);
        Assert.True(record.HasRenderedError);
    }

    [Fact]
    public void BundleIncludesBoundedMonitorTimeline()
    {
        var supportSource = ReadSource("src", "GPTDeskTop", "Services", "SupportBundleService.cs");
        var traceSource = ReadSource("src", "GPTDeskTop", "Services", "MonitorDiagnosticTraceService.cs");

        Assert.Contains("MonitorDiagnosticTraceService.EnsureStarted", supportSource, StringComparison.Ordinal);
        Assert.Contains("MonitorDiagnosticTraceService.ReadBundleTail", supportSource, StringComparison.Ordinal);
        Assert.Contains("monitor-diagnostics.jsonl", supportSource, StringComparison.Ordinal);
        Assert.Contains("MaxTraceBytes = 4L * 1024 * 1024", traceSource, StringComparison.Ordinal);
        Assert.Contains("DefaultBundleTailBytes = 768 * 1024", traceSource, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}

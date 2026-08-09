using GPTDeskTop.Models;
using GPTDeskTop.UI;

namespace GPTDeskTop.RuntimeTests;

public sealed class HistoryWorkspaceLogicTests
{
    private static MessageLog Log(
        long id,
        string flow,
        string status,
        string chat = "Chat A",
        string prompt = "prompt",
        string response = "response")
        => new()
        {
            Id = id,
            Timestamp = new DateTime(2026, 8, 9, 11, 0, (int)id),
            MonitorId = id,
            TabId = $"tab-{id}",
            TabTitle = chat,
            Direction = flow,
            Prompt = prompt,
            Response = response,
            Status = status
        };

    [Fact]
    public void FilterCombinesSearchFlowAndStatusWithoutChangingSourceOrder()
    {
        var source = new[]
        {
            Log(1, "Outbound", "Sent", response: "alpha response"),
            Log(2, "Inbound", "Timeout", response: "alpha timeout"),
            Log(3, "Outbound", "Deferred", response: "alpha deferred"),
            Log(4, "Outbound", "Sent", response: "beta response")
        };

        var filtered = HistoryWorkspaceLogic.Filter(source, "alpha", "Outbound", HistoryWorkspaceLogic.Success);

        var item = Assert.Single(filtered);
        Assert.Equal(1, item.Id);
    }

    [Theory]
    [InlineData("Fatal Error", HistoryWorkspaceLogic.Issues)]
    [InlineData("Delivery verified", HistoryWorkspaceLogic.Success)]
    [InlineData("Rotation deferred for retry", HistoryWorkspaceLogic.Deferred)]
    [InlineData("Observed", HistoryWorkspaceLogic.Other)]
    public void StatusCategoryUsesOperationalSemantics(string status, string expected)
        => Assert.Equal(expected, HistoryWorkspaceLogic.GetStatusCategory(status));

    [Fact]
    public void SearchCoversChatPromptResponseStatusTabAndMonitorIdentity()
    {
        var log = Log(42, "System", "Observed", "Important Chat", "needle prompt", "body");
        var source = new[] { log };

        Assert.Single(HistoryWorkspaceLogic.Filter(source, "important", "All", "All"));
        Assert.Single(HistoryWorkspaceLogic.Filter(source, "needle", "All", "All"));
        Assert.Single(HistoryWorkspaceLogic.Filter(source, "tab-42", "All", "All"));
        Assert.Single(HistoryWorkspaceLogic.Filter(source, "42", "All", "All"));
        Assert.Empty(HistoryWorkspaceLogic.Filter(source, "missing", "All", "All"));
    }

    [Fact]
    public void CsvEscapesQuotesCommasAndNewlinesAndExportsOnlyProvidedRows()
    {
        var visible = new[]
        {
            Log(1, "Outbound", "Sent", "Chat, One", "say \"hello\"", "line1\nline2")
        };

        var csv = HistoryWorkspaceLogic.ToCsv(visible);

        Assert.Contains("Time,MonitorId,TabId,Chat,Flow,Prompt,Response,Status", csv, StringComparison.Ordinal);
        Assert.Contains("\"Chat, One\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"say \"\"hello\"\"\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"line1\nline2\"", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("tab-2", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void ClipboardTextPreservesOperationalContext()
    {
        var log = Log(7, "Inbound", "Recovered", "Recovery Chat", "continue", "done");

        var text = HistoryWorkspaceLogic.ToClipboardText(log);

        Assert.Contains("Monitor: 7", text, StringComparison.Ordinal);
        Assert.Contains("Chat: Recovery Chat", text, StringComparison.Ordinal);
        Assert.Contains("Flow: Inbound", text, StringComparison.Ordinal);
        Assert.Contains("Status: Recovered", text, StringComparison.Ordinal);
        Assert.Contains("continue", text, StringComparison.Ordinal);
        Assert.Contains("done", text, StringComparison.Ordinal);
    }
}

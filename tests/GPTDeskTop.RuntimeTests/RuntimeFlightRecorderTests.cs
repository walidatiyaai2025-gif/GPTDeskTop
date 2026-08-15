using System.Text.Json;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class RuntimeFlightRecorderTests
{
    [Fact]
    public void RecorderKeepsOnlyNewestThousandEventsInSequenceOrder()
    {
        RuntimeFlightRecorder.ResetForTests();
        for (var i = 0; i < RuntimeFlightRecorder.Capacity + 7; i++)
            RuntimeFlightRecorder.Record("Test", "Event", reason: $"r{i}");

        var snapshot = RuntimeFlightRecorder.Snapshot();
        Assert.Equal(1000, snapshot.EventCount);
        Assert.Equal(8, snapshot.FirstSequence);
        Assert.Equal(1007, snapshot.LastSequence);
        Assert.Equal(snapshot.Events.OrderBy(item => item.Sequence).Select(item => item.Sequence), snapshot.Events.Select(item => item.Sequence));
    }

    [Fact]
    public async Task RecorderIsThreadSafeAndSequenceNumbersStayUnique()
    {
        RuntimeFlightRecorder.ResetForTests();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 300; i++)
                RuntimeFlightRecorder.Record("Concurrent", "Record", reason: $"w{worker}");
        })));

        var snapshot = RuntimeFlightRecorder.Snapshot();
        Assert.Equal(1000, snapshot.EventCount);
        Assert.Equal(1000, snapshot.Events.Select(item => item.Sequence).Distinct().Count());
        Assert.True(snapshot.Events.Zip(snapshot.Events.Skip(1), (left, right) => left.Sequence < right.Sequence).All(value => value));
    }

    [Fact]
    public void ScopeCorrelatesMonitorAndHashesBrowserIdentifiers()
    {
        RuntimeFlightRecorder.ResetForTests();
        const string tabSecret = "6D0DEABA64B994C-super-secret-target";
        const string conversationSecret = "https://chatgpt.com/c/6a80dead-beef-secret?token=private";
        using (RuntimeFlightRecorder.BeginScope(17, tabSecret, conversationSecret))
            RuntimeFlightRecorder.Record("Delivery", "PhysicalSubmitRequested");

        var item = Assert.Single(RuntimeFlightRecorder.Snapshot().Events);
        Assert.Equal(17, item.MonitorId);
        Assert.StartsWith("tab:", item.TabKey);
        Assert.StartsWith("conv:", item.ConversationKey);
        var json = JsonSerializer.Serialize(item);
        Assert.DoesNotContain(tabSecret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("6a80dead-beef-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token=private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chatgpt.com", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://example.com/private")]
    [InlineData("Authorization: Bearer abc")]
    [InlineData("Cookie=session-secret")]
    [InlineData("localStorage-secret")]
    public void SensitiveReasonTokensAreRedacted(string sensitive)
    {
        RuntimeFlightRecorder.ResetForTests();
        RuntimeFlightRecorder.Record("Security", "Observed", reason: sensitive);
        var item = Assert.Single(RuntimeFlightRecorder.Snapshot().Events);
        Assert.Equal("redacted", item.Reason);
        Assert.DoesNotContain(sensitive, JsonSerializer.Serialize(item), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposerAndVerifiedSendDiagnosticsProjectIntoSameMonitorScope()
    {
        RuntimeFlightRecorder.ResetForTests();
        using (RuntimeFlightRecorder.BeginScope(15, "target-15"))
        {
            ChatComposerInterlockPolicy.DecideBeforeSubmit(false, true, true, true, true, false);
            VerifiedSendDiagnostics.Record("ReceiptConfirmed", "immediate-user-turn-observed", 1);
        }

        var events = RuntimeFlightRecorder.Snapshot().Events;
        Assert.Contains(events, item => item.MonitorId == 15 && item.Category == "Composer" && item.Action == "ReadyToSend");
        Assert.Contains(events, item => item.MonitorId == 15 && item.Category == "VerifiedSend" && item.Action == "ReceiptConfirmed");
        Assert.All(events, item => Assert.StartsWith("tab:", item.TabKey));
    }

    [Fact]
    public void UiObserverNeverReadsControlTextOrUserInputProperties()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "RuntimeFlightUiObserver.cs");
        Assert.DoesNotContain(".Text", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBox", source, StringComparison.Ordinal);
        Assert.Contains("control.Name", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}

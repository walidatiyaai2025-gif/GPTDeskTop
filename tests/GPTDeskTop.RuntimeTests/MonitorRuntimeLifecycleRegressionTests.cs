namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorRuntimeLifecycleRegressionTests
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
    public void InspectorDoesNotExposeRawTaskStatusAsOperatorWorkerState()
    {
        var inspector = ReadSource("src", "GPTDeskTop", "Services", "RuntimeInspectorService.cs");

        Assert.Contains("MonitorRuntimeDiagnosticReader.Capture(monitor)", inspector, StringComparison.Ordinal);
        Assert.Contains("WorkerStatus = diagnostic.LifecycleStatus", inspector, StringComparison.Ordinal);
        Assert.Contains("diagnostic.LifecycleStatus", inspector, StringComparison.Ordinal);
        Assert.Contains("diagnostic.RawTaskStatus", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("worker?.Status.ToString() ?? \"unknown\"", inspector, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveIncompleteWorkerIsClassifiedRunningRegardlessOfAsyncTaskStatus()
    {
        var reader = ReadSource("src", "GPTDeskTop", "Services", "MonitorRuntimeDiagnosticReader.cs");
        var start = reader.IndexOf("internal static string Classify", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var method = reader[start..];

        Assert.Contains("if (worker.IsFaulted) return \"Faulted\";", method, StringComparison.Ordinal);
        Assert.Contains("if (cancellationRequested || stopOwnsCleanup) return \"Stopping\";", method, StringComparison.Ordinal);
        Assert.Contains("if (worker.IsCanceled) return \"Canceled\";", method, StringComparison.Ordinal);
        Assert.Contains("if (worker.IsCompleted) return \"Completed\";", method, StringComparison.Ordinal);
        Assert.Contains("return \"Running\";", method, StringComparison.Ordinal);
        Assert.DoesNotContain("return worker.Status.ToString()", method, StringComparison.Ordinal);
    }

    [Fact]
    public void RawTaskStatusRemainsAvailableOnlyAsSecondaryDiagnostic()
    {
        var reader = ReadSource("src", "GPTDeskTop", "Services", "MonitorRuntimeDiagnosticReader.cs");

        Assert.Contains("string RawTaskStatus", reader, StringComparison.Ordinal);
        Assert.Contains("worker?.Status.ToString() ?? \"Unknown\"", reader, StringComparison.Ordinal);
        Assert.Contains("CancellationRequested", reader, StringComparison.Ordinal);
        Assert.Contains("ObservedSinceUtc", reader, StringComparison.Ordinal);
        Assert.Contains("ObservedForSeconds", reader, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeDictionaryIsSnapshottedUnderTheMonitorServiceSynchronizationGate()
    {
        var reader = ReadSource("src", "GPTDeskTop", "Services", "MonitorRuntimeDiagnosticReader.cs");

        Assert.Contains("GetField(\"_running\"", reader, StringComparison.Ordinal);
        Assert.Contains("GetField(\"_sync\"", reader, StringComparison.Ordinal);
        Assert.Contains("lock (syncRoot)", reader, StringComparison.Ordinal);
        Assert.Contains("foreach (System.Collections.DictionaryEntry entry in running)", reader, StringComparison.Ordinal);
    }
}

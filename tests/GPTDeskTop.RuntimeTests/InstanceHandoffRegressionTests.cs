namespace GPTDeskTop.RuntimeTests;

public sealed class InstanceHandoffRegressionTests
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
    public void StartupAcquiresExclusiveInstanceOwnershipBeforeDatabaseRuntime()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        var ownership = source.IndexOf("InstanceHandoffCoordinator.AcquireOrTakeOver()", StringComparison.Ordinal);
        var database = source.IndexOf("database = new LocalDatabase(databasePath);", StringComparison.Ordinal);

        Assert.True(ownership >= 0, "Program must acquire or safely take over single-instance ownership.");
        Assert.True(database > ownership, "SQLite/runtime startup must happen only after exclusive instance ownership is established.");
        Assert.Contains("if (!instanceStartup.IsPrimary)", source, StringComparison.Ordinal);
        Assert.Contains("The second runtime was not started", ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TakeoverCarriesAbsoluteDatabaseAndEffectiveConfigurationAcrossExeFolders()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");
        var coordinator = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");

        Assert.Contains("var config = takeover?.Config ?? AppConfig.Load();", source, StringComparison.Ordinal);
        Assert.Contains("takeover?.DatabasePath", source, StringComparison.Ordinal);
        Assert.Contains("ResolveDatabasePath", source, StringComparison.Ordinal);
        Assert.Contains("config.Database.FileName = databasePath;", source, StringComparison.Ordinal);
        Assert.Contains("string DatabasePath", coordinator, StringComparison.Ordinal);
        Assert.Contains("AppConfig Config", coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveOperatorWorkspaceIsPersistedBeforeTakeoverOfferIsCaptured()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        var persist = source.IndexOf("await PersistOperatorLayoutForInstanceHandoffAsync(mainForm, cancellationToken);", StringComparison.Ordinal);
        var snapshot = source.IndexOf("var savedMonitors = await database.GetSavedMonitorsAsync(cancellationToken);", persist, StringComparison.Ordinal);

        Assert.True(persist >= 0 && snapshot > persist, "Current window/splitter state must be saved before the handoff snapshot is offered.");
        Assert.Contains("PersistOperatorLayoutAsync", source, StringComparison.Ordinal);
        Assert.Contains("BindingFlags.Instance | BindingFlags.NonPublic", source, StringComparison.Ordinal);
        Assert.Contains("mainForm.BeginInvoke", source, StringComparison.Ordinal);
        Assert.Contains("completion.Task.WaitAsync(cancellationToken)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommittedTakeoverStopsWorkersButLeavesChromeAndGeneratingChatsAlive()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");
        var start = source.IndexOf("private static async Task CompleteCommittedInstanceHandoffAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private static async Task FinalizeGracefulShutdownAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("monitor.StopAllAsync()", method, StringComparison.Ordinal);
        Assert.Contains("CrashRecoveryStateService.MarkCleanShutdownAsync", method, StringComparison.Ordinal);
        Assert.Contains("Environment.Exit(0);", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseAllMonitorTabsAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Kill", method, StringComparison.Ordinal);
        Assert.Contains("in-progress ChatGPT generation alive", method, StringComparison.Ordinal);
    }

    [Fact]
    public void NewInstanceWaitsForOldOwnershipReleaseAfterAcknowledgingPayload()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");

        var request = source.IndexOf("var offer = RequestTakeover();", StringComparison.Ordinal);
        var wait = source.IndexOf("WaitForMutex(takeoverMutex, OwnershipTimeout)", StringComparison.Ordinal);
        Assert.True(request >= 0 && wait > request);
        Assert.Contains("InstanceHandoffAck", source, StringComparison.Ordinal);
        Assert.Contains("MutexName", source, StringComparison.Ordinal);
        Assert.Contains("OwnershipTimeout = TimeSpan.FromSeconds(25)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatalRestartRaceMayClaimOnlyAReleasedOrAbandonedOwnerMutex()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");

        var noOffer = source.IndexOf("if (offer is null)", StringComparison.Ordinal);
        var orphanWait = source.IndexOf("WaitForMutex(orphanedOwnerMutex, OrphanedOwnerTimeout)", noOffer, StringComparison.Ordinal);
        var failClosed = source.IndexOf("The second runtime was not started", orphanWait, StringComparison.Ordinal);

        Assert.True(noOffer >= 0 && orphanWait > noOffer && failClosed > orphanWait);
        Assert.Contains("OrphanedOwnerTimeout = TimeSpan.FromSeconds(2)", source, StringComparison.Ordinal);
        Assert.Contains("catch (AbandonedMutexException)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyPreviouslyRunningEnabledMonitorsAreResumed()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("RunningMonitorIds", source, StringComparison.Ordinal);
        Assert.Contains("requestedIds.Contains(saved.Id)", source, StringComparison.Ordinal);
        Assert.Contains("if (!savedMonitor.Enabled) continue;", source, StringComparison.Ordinal);
        Assert.Contains("SavedMonitorTabResolver.Resolve(savedMonitor, tabs)", source, StringComparison.Ordinal);
        Assert.Contains("monitorService.StartMonitorAsync(savedMonitor, tab)", source, StringComparison.Ordinal);
        Assert.Contains("monitor.IsMonitorRunning(saved.Id)", program, StringComparison.Ordinal);
    }

    [Fact]
    public void TakeoverProtocolIsTwoPhaseAndFailsClosed()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");

        Assert.Contains("await writer.WriteLineAsync(JsonSerializer.Serialize(offer))", source, StringComparison.Ordinal);
        Assert.Contains("var ackLine = await reader.ReadLineAsync", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.CompareExchange(ref _takeoverCommitted, 1, 0)", source, StringComparison.Ordinal);
        Assert.Contains("if (offer is null)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseMainWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Kill(", source, StringComparison.Ordinal);
    }
}

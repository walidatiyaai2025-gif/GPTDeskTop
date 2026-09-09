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
    public void StartupAcquiresMonitorOnlySingletonBeforeDatabaseRuntime()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        var ownership = source.IndexOf("using var singleInstance = new Mutex", StringComparison.Ordinal);
        var database = source.IndexOf("database = new LocalDatabase(databasePath);", StringComparison.Ordinal);

        Assert.True(ownership >= 0, "Program must acquire the Monitor Only singleton before creating runtime state.");
        Assert.True(database > ownership, "SQLite/runtime startup must happen only after exclusive instance ownership is established.");
        Assert.Contains("GPTDeskTop-MonitorOnly-SingleInstance", source, StringComparison.Ordinal);
        Assert.Contains("if (!ownsMutex)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InstanceHandoffCoordinator.AcquireOrTakeOver", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyTakeoverContractRemainsReusableButMonitorOnlyDoesNotActivateIt()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");
        var coordinator = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");

        Assert.Contains("var config = AppConfig.Load();", source, StringComparison.Ordinal);
        Assert.Contains("ResolveDatabasePath", source, StringComparison.Ordinal);
        Assert.Contains("config.Database.FileName = databasePath;", source, StringComparison.Ordinal);
        Assert.Contains("string DatabasePath", coordinator, StringComparison.Ordinal);
        Assert.Contains("AppConfig Config", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("takeover?.Config", source, StringComparison.Ordinal);
        Assert.DoesNotContain("takeover?.DatabasePath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorOnlyStartupDoesNotCaptureLegacyOperatorWorkspaceForTakeover()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("MonitorOnlyStartupGate.Run(database);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistOperatorLayoutForInstanceHandoffAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mainForm.BeginInvoke", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InstanceHandoffCoordinator", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorOnlyHasNoCommittedLegacyTakeoverShutdownPath()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");
        var ownership = ReadSource("src", "GPTDeskTop", "Services", "SimpleMonitorChromeOwnershipGate.cs");

        Assert.Contains("GPTDeskTop-MonitorOnly-SingleInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CompleteCommittedInstanceHandoffAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FinalizeGracefulShutdownAsync", source, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", ownership, StringComparison.Ordinal);
        Assert.Contains("Ordinary Chrome is untouched", ownership, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacementOpensOwnershipMutexAndPreflightsBeforeReadyThenWaitsAfterCommit()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");

        var mutex = source.IndexOf("takeoverMutex = new Mutex(initiallyOwned: false, MutexName);", StringComparison.Ordinal);
        var request = source.IndexOf("var offer = RequestTakeover(takeoverMutex);", mutex, StringComparison.Ordinal);
        var wait = source.IndexOf("WaitForMutex(takeoverMutex, OwnershipTimeout)", request, StringComparison.Ordinal);
        var methodStart = source.IndexOf("private static InstanceHandoffOffer? RequestTakeover(Mutex takeoverMutex)", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static string? ValidateOfferForCommit", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];
        var preflight = method.IndexOf("ValidateOfferForCommit(offer, takeoverMutex)", StringComparison.Ordinal);
        var ready = method.IndexOf("new InstanceHandoffReady(request.RequestId, Environment.ProcessId)", StringComparison.Ordinal);
        var commit = method.IndexOf("Deserialize<InstanceHandoffCommit>", StringComparison.Ordinal);
        var returnOffer = method.IndexOf("return offer;", commit, StringComparison.Ordinal);

        Assert.True(mutex >= 0 && request > mutex && wait > request,
            "The replacement must open the ownership mutex before negotiation and wait on that already-open mutex only after commit acceptance.");
        Assert.True(preflight >= 0 && ready > preflight && commit > ready && returnOffer > commit,
            "Client ordering must be offer -> preflight -> Ready -> CommitAccepted -> ownership wait.");
        Assert.Contains("Path.IsPathFullyQualified(offer.DatabasePath)", source, StringComparison.Ordinal);
        Assert.Contains("File.Exists(offer.DatabasePath)", source, StringComparison.Ordinal);
        Assert.Contains("IsProcessAlive(offer.PreviousProcessId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerRequiresCorrelatedLiveReplacementReadinessBeforeCommittedShutdown()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");
        var serverStart = source.IndexOf("private async Task RunServerAsync", StringComparison.Ordinal);
        var clientStart = source.IndexOf("private static InstanceHandoffOffer? RequestTakeover", serverStart, StringComparison.Ordinal);
        var server = source[serverStart..clientStart];

        var offerWrite = server.IndexOf("await writer.WriteLineAsync(JsonSerializer.Serialize(offer))", StringComparison.Ordinal);
        var readyRead = server.IndexOf("Deserialize<InstanceHandoffReady>", offerWrite, StringComparison.Ordinal);
        var correlation = server.IndexOf("ready.ProcessId != request.ProcessId", readyRead, StringComparison.Ordinal);
        var liveness = server.IndexOf("!IsProcessAlive(request.ProcessId)", correlation, StringComparison.Ordinal);
        var commitMarker = server.IndexOf("Interlocked.CompareExchange(ref _takeoverCommitted, 1, 0)", liveness, StringComparison.Ordinal);
        var commitWrite = server.IndexOf("new InstanceHandoffCommit(", commitMarker, StringComparison.Ordinal);
        var shutdown = server.IndexOf("await committedTakeoverShutdown()", commitWrite, StringComparison.Ordinal);

        Assert.True(offerWrite >= 0 && readyRead > offerWrite && correlation > readyRead && liveness > correlation);
        Assert.True(commitMarker > liveness && commitWrite > commitMarker && shutdown > commitWrite,
            "The old runtime must verify correlated live readiness, send commit acceptance, and only then begin committed shutdown.");
        Assert.Contains("Interlocked.Exchange(ref _takeoverCommitted, 0);", server, StringComparison.Ordinal);
        Assert.Contains("Replacement process exited before takeover commit.", server, StringComparison.Ordinal);
    }

    [Fact]
    public void PostReadyCommitAmbiguityFailsClosedInsteadOfUsingOrphanFallback()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");
        var methodStart = source.IndexOf("private static InstanceHandoffOffer? RequestTakeover(Mutex takeoverMutex)", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static string? ValidateOfferForCommit", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        var readyFlag = method.IndexOf("readySent = true;", StringComparison.Ordinal);
        var uncertain = method.IndexOf("if (readySent)", readyFlag, StringComparison.Ordinal);
        var failClosed = method.IndexOf("could not confirm whether the previous runtime committed shutdown", uncertain, StringComparison.Ordinal);

        Assert.True(readyFlag >= 0 && uncertain > readyFlag && failClosed > uncertain);
        Assert.Contains("An uncertain post-Ready outcome throws instead", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InstanceHandoffAck", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatalRestartRaceMayClaimOnlyAReleasedOrAbandonedOwnerMutex()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");

        var noOffer = source.IndexOf("if (offer is null)", StringComparison.Ordinal);
        var orphanWait = source.IndexOf("WaitForMutex(takeoverMutex, OrphanedOwnerTimeout)", noOffer, StringComparison.Ordinal);
        var failClosed = source.IndexOf("The second runtime was not started", orphanWait, StringComparison.Ordinal);

        Assert.True(noOffer >= 0 && orphanWait > noOffer && failClosed > orphanWait);
        Assert.Contains("OrphanedOwnerTimeout = TimeSpan.FromSeconds(2)", source, StringComparison.Ordinal);
        Assert.Contains("catch (AbandonedMutexException)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyResumeCoordinatorRemainsBoundedButMonitorOnlyNeverAutoResumesIt()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");
        var program = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("RunningMonitorIds", source, StringComparison.Ordinal);
        Assert.Contains(".Where(id => id > 0)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var monitorId in requestedIds)", source, StringComparison.Ordinal);
        Assert.Contains("if (!savedById.TryGetValue(monitorId, out var savedMonitor))", source, StringComparison.Ordinal);
        Assert.Contains("if (!savedMonitor.Enabled)", source, StringComparison.Ordinal);
        Assert.Contains("pendingIds.Add(monitorId)", source, StringComparison.Ordinal);
        Assert.Contains("SavedMonitorTabResolver.Resolve(savedMonitor, tabs)", source, StringComparison.Ordinal);
        Assert.Contains("monitorService.StartMonitorAsync(savedMonitor, tab)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("monitor.IsMonitorRunning(saved.Id)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ResumeRunningMonitorsAsync", program, StringComparison.Ordinal);
    }

    [Fact]
    public void TakeoverProtocolIsThreePhaseAndFailsClosed()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "InstanceHandoffCoordinator.cs");

        Assert.Contains("await writer.WriteLineAsync(JsonSerializer.Serialize(offer))", source, StringComparison.Ordinal);
        Assert.Contains("InstanceHandoffReady", source, StringComparison.Ordinal);
        Assert.Contains("InstanceHandoffCommit", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.CompareExchange(ref _takeoverCommitted, 1, 0)", source, StringComparison.Ordinal);
        Assert.Contains("if (offer is null)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InstanceHandoffAck", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseMainWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Kill(", source, StringComparison.Ordinal);
    }
}

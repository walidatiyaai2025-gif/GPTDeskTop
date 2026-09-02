namespace GPTDeskTop.RuntimeTests;

public sealed class GlobalMonitorSendSerializationRegressionTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(segments)));

    [Fact]
    public void OutboundDeliveryUsesOneProcessWideGateAndMandatoryCooldown()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs"));

        Assert.Contains("private readonly SemaphoreSlim _globalGate = new(1, 1);", source, StringComparison.Ordinal);
        Assert.Contains("GlobalSendCooldown = TimeSpan.FromSeconds(15)", source, StringComparison.Ordinal);
        Assert.Contains("WAITING_FOR_GLOBAL_SLOT", source, StringComparison.Ordinal);
        Assert.Contains("await _globalGate.WaitAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("_ = ReleaseAfterCooldownAsync(monitorId, leaseGeneration.Value);", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(GlobalSendCooldown)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalGateIsHeldAcrossAssistantGenerationInsteadOfOnlyPhysicalSubmit()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs"));

        var sendStart = source.IndexOf("public async Task<bool> SendOnceAsync", StringComparison.Ordinal);
        var markCompleted = source.IndexOf("public void MarkCompleted", sendStart, StringComparison.Ordinal);
        Assert.True(sendStart >= 0 && markCompleted > sendStart);
        var sendMethod = source[sendStart..markCompleted];

        Assert.Contains("releaseGlobalOnExit = false;", sendMethod, StringComparison.Ordinal);
        Assert.Contains("ScheduleHardCeilingRelease", sendMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("finally\n        {\n            _globalGate.Release();", sendMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void StalledOwnerCannotBlockEveryOtherMonitorForever()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs"));

        Assert.Contains("GlobalLeaseHardCeiling = TimeSpan.FromMinutes(20)", source, StringComparison.Ordinal);
        Assert.Contains("STALLED:", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseAtHardCeilingAsync", source, StringComparison.Ordinal);
        Assert.Contains("ReleaseGlobalLeaseIfOwned", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitAbandonmentCanReclaimAnOwnedGlobalSlot()
    {
        var source = File.ReadAllText(RepositoryPath(
            "src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs"));

        Assert.Contains("public void AbandonMonitor(long monitorId", source, StringComparison.Ordinal);
        Assert.Contains("monitor-stopped", source, StringComparison.Ordinal);
        Assert.Contains("_globalOwnerMonitorId = null;", source, StringComparison.Ordinal);
        Assert.Contains("_globalGate.Release();", source, StringComparison.Ordinal);
    }
}

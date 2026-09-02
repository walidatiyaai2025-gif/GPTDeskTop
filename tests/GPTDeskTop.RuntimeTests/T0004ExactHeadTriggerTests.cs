namespace GPTDeskTop.RuntimeTests;

public sealed class T0004ExactHeadTriggerTests
{
    private static string Source(params string[] parts)
        => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", Path.Combine(parts))));

    [Fact]
    public void ExactHeadContainsDevelopmentMessagesOwnershipBoundary()
    {
        var monitor = Source("Services", "ChatGptMonitorService.cs");
        var binding = Source("Services", "DevelopmentTaskEngine", "DevelopmentTaskRuntimeBinding.cs");
        var coordinator = Source("Services", "DevelopmentTaskEngine", "DevelopmentTaskRuntimeCoordinator.cs");

        Assert.Contains("DevelopmentMonitorLegacyStartSuppressed", monitor, StringComparison.Ordinal);
        Assert.Contains("ResolveEnabledRecipientsAsync", binding, StringComparison.Ordinal);
        Assert.Contains("await _engine.StartAsync(planId, planTitle", coordinator, StringComparison.Ordinal);
    }
}

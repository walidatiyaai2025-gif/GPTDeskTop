namespace GPTDeskTop.RuntimeTests;

public sealed class DuplicateOwnershipRepairUiRegressionTests
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
    public void RuntimeHealthOffersRepairForDuplicateOwnershipBlockers()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "RuntimeHealthControl.cs");

        Assert.Contains("(invalidMonitorCount > 0 || duplicateMonitorCount > 0)", source, StringComparison.Ordinal);
        Assert.Contains("Use Repair… to move a duplicate owner", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairDialogListsBothBlockerTypesAndOnlyUnownedStableTargets()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorIdentityRepairForm.cs");

        Assert.Contains("MonitorConversationOwnership.FindDuplicateMonitorIds(monitors)", source, StringComparison.Ordinal);
        Assert.Contains("duplicateIds.Contains(saved.Id)", source, StringComparison.Ordinal);
        Assert.Contains("!ownedConversationUrls.Contains(tab.Url)", source, StringComparison.Ordinal);
        Assert.Contains("new MonitorChoice(saved, duplicateIds.Contains(saved.Id))", source, StringComparison.Ordinal);
        Assert.Contains("_duplicateRepairService.RebindAsync", source, StringComparison.Ordinal);
        Assert.Contains("Duplicate owner", source, StringComparison.Ordinal);
        Assert.Contains("Invalid identity", source, StringComparison.Ordinal);
    }
}

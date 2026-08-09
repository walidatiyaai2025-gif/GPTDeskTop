namespace GPTDeskTop.RuntimeTests;

public sealed class MonitorIdentityRepairUiRegressionTests
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
    public void RepairDialogIsDpiSafeAccessibleAndFiltersBothSidesByIdentityContract()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorIdentityRepairForm.cs");

        Assert.Contains("AutoScaleMode = AutoScaleMode.Dpi", source, StringComparison.Ordinal);
        Assert.Contains("AccessibleName = \"Monitor conversation identity repair\"", source, StringComparison.Ordinal);
        Assert.Contains("!RuntimeHealthPresentation.IsChatGptConversationUrl(saved.Url)", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url)", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(5)", source, StringComparison.Ordinal);
        Assert.Contains("DropDownStyle = ComboBoxStyle.DropDownList", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairDialogUsesDedicatedServiceConfirmationAndNeverDeletesOrClearsRecoveryState()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MonitorIdentityRepairForm.cs");

        Assert.Contains("new MonitorIdentityRepairService(database)", source, StringComparison.Ordinal);
        Assert.Contains("Confirm Monitor Rebind", source, StringComparison.Ordinal);
        Assert.Contains("_repairService.RebindAsync", source, StringComparison.Ordinal);
        Assert.Contains("monitor ID, history, automation settings and rotation count will be preserved", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeleteMonitorAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CrashRecoveryPending\", \"0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairServiceGuardsSourceTargetAndDuplicateOwnershipAndWritesReceipt()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "MonitorIdentityRepairService.cs");

        Assert.Contains("if (RuntimeHealthPresentation.IsChatGptConversationUrl(monitor.Url))", source, StringComparison.Ordinal);
        Assert.Contains("if (!RuntimeHealthPresentation.IsChatGptConversationUrl(targetTab.Url))", source, StringComparison.Ordinal);
        Assert.Contains("saved.Id != monitor.Id", source, StringComparison.Ordinal);
        Assert.Contains("ChatGptConversationIdentity.IsSame(saved.Url, targetTab.Url)", source, StringComparison.Ordinal);
        Assert.Contains("MonitorConversationIdentityRebound", source, StringComparison.Ordinal);
        Assert.Contains("CrashRecoveryPending", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSettingAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteMonitorAsync", source, StringComparison.Ordinal);
    }
}
namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskDeliveryReceiptTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GPTDeskTop", Path.Combine(segments)));

    [Fact]
    public void DeliveryExtensionStoresMonitorTabMessageAndFingerprint()
    {
        var source = File.ReadAllText(RepositoryPath("Services", "DevelopmentTaskEngine", "DevelopmentTaskEngineDeliveryExtensions.cs"));
        Assert.Contains("LastMonitorId", source, StringComparison.Ordinal);
        Assert.Contains("LastTabId", source, StringComparison.Ordinal);
        Assert.Contains("LastDeliveredMessageIndex", source, StringComparison.Ordinal);
        Assert.Contains("LastDeliveredMessageFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("CheckpointAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedSendPathUsesReceiptBeforeAdvance()
    {
        var source = File.ReadAllText(RepositoryPath("Services", "DevelopmentTaskEngine", "DevelopmentTaskDeliveryCoordinator.cs"));
        var checkpoint = source.IndexOf("_checkpointAfterDelivery", StringComparison.Ordinal);
        var advance = source.IndexOf("await _engine.AdvanceAsync()", StringComparison.Ordinal);
        Assert.True(checkpoint >= 0);
        Assert.True(advance > checkpoint);
    }

    [Fact]
    public void ChromeVerifiedSendUsesTurnStateForRepeatedTextReceipt()
    {
        var source = File.ReadAllText(RepositoryPath("Services", "ChromeDevToolsService.cs"));
        Assert.Contains("bool requireNewTurn = false", source, StringComparison.Ordinal);
        Assert.Contains("MonitorDeliveryRecoveryPolicy.CanReuseMatchingUserTailAsReceipt", source, StringComparison.Ordinal);
        Assert.Contains("deliveryState.AssistantCount", source, StringComparison.Ordinal);
        Assert.Contains("deliveryState.IsGenerating", source, StringComparison.Ordinal);
        Assert.Contains("current.Count > before.Count && string.Equals(current.LastText, expected, StringComparison.Ordinal)", source, StringComparison.Ordinal);
    }
}

using GPTDeskTop.Services.DevelopmentTaskEngine;

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
    public void ChromeVerifiedSendHasCrashSafeAlreadyDeliveredGuard()
    {
        var source = File.ReadAllText(RepositoryPath("Services", "ChromeDevToolsService.cs"));

        // Normal verified sends retain the crash-safe already-delivered shortcut. Restart recovery
        // can opt out with requireNewTurn=true so a repeated continuation such as "كمل" must create
        // a new user turn instead of accepting an older identical turn as the receipt.
        Assert.Contains("bool requireNewTurn = false", source, StringComparison.Ordinal);
        Assert.Contains("if (!requireNewTurn && string.Equals(before.LastText, expected, StringComparison.Ordinal)) return true;", source, StringComparison.Ordinal);
        Assert.Contains("current.Count > before.Count && string.Equals(current.LastText, expected, StringComparison.Ordinal)", source, StringComparison.Ordinal);
    }
}

using GPTDeskTop.Services.DevelopmentTaskEngine;

namespace GPTDeskTop.RuntimeTests;

public sealed class DevelopmentTaskDeliveryReceiptTests
{
    [Fact]
    public void DeliveryExtensionStoresMonitorTabMessageAndFingerprint()
    {
        var source = File.ReadAllText(Path.Combine("..", "..", "..", "..", "src", "GPTDeskTop", "Services", "DevelopmentTaskEngine", "DevelopmentTaskEngineDeliveryExtensions.cs"));

        Assert.Contains("LastMonitorId", source, StringComparison.Ordinal);
        Assert.Contains("LastTabId", source, StringComparison.Ordinal);
        Assert.Contains("LastDeliveredMessageIndex", source, StringComparison.Ordinal);
        Assert.Contains("LastDeliveredMessageFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("CheckpointAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedSendPathUsesReceiptBeforeAdvance()
    {
        var source = File.ReadAllText(Path.Combine("..", "..", "..", "..", "src", "GPTDeskTop", "Services", "DevelopmentTaskEngine", "DevelopmentTaskDeliveryCoordinator.cs"));

        var checkpoint = source.IndexOf("_checkpointAfterDelivery", StringComparison.Ordinal);
        var advance = source.IndexOf("await _engine.AdvanceAsync()", StringComparison.Ordinal);

        Assert.True(checkpoint >= 0);
        Assert.True(advance > checkpoint);
    }

    [Fact]
    public void ChromeVerifiedSendHasCrashSafeAlreadyDeliveredGuard()
    {
        var source = File.ReadAllText(Path.Combine("..", "..", "..", "..", "src", "GPTDeskTop", "Services", "ChromeDevToolsService.cs"));

        Assert.Contains("if (string.Equals(before.LastText, expected, StringComparison.Ordinal)) return true;", source, StringComparison.Ordinal);
    }
}

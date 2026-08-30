using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SavedMonitorLiveGridWiringTests
{
    [Fact]
    public void DeliveryStatusPayloadContainsOnlyPrivacySafeOperatorFields()
    {
        var assembly = typeof(SavedMonitorLivePresentation).Assembly;
        var statusType = assembly.GetType("GPTDeskTop.Runtime.OutboundDeliveryStatus", throwOnError: true)!;
        var properties = statusType.GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray();

        Assert.Equal(
            new[] { "MonitorId", "Phase", "PhysicalSendCount", "UpdatedUtc" }.OrderBy(name => name),
            properties);
        Assert.DoesNotContain("ConversationKey", properties);
        Assert.DoesNotContain("MessageFingerprint", properties);
        Assert.DoesNotContain("Reason", properties);
        Assert.DoesNotContain("Message", properties);
        Assert.DoesNotContain("Url", properties);
        Assert.DoesNotContain("TabKey", properties);
    }

    [Fact]
    public void SavedMonitorGridUsesPerMonitorDeliveryEventsAndNotGlobalLastSendDiagnostics()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "SavedMonitorHealthGridExperience.cs");

        Assert.Contains("_delivery.StatusChanged += OnDeliveryStatusChanged;", source, StringComparison.Ordinal);
        Assert.Contains("status.MonitorId", source, StringComparison.Ordinal);
        Assert.Contains("SavedMonitorLivePresentation.Overlay", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VerifiedSendDiagnostics.Last", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatComposerDecisionDiagnostics.Last", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusObserversAreIsolatedFromExactlyOnceDeliveryPath()
    {
        var source = ReadSource("src", "GPTDeskTop", "Runtime", "OutboundDeliveryCoordinator.cs");

        Assert.Contains("foreach (var subscriber in handlers.GetInvocationList())", source, StringComparison.Ordinal);
        Assert.Contains("try { ((Action<OutboundDeliveryStatus>)subscriber)(status); }", source, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex) { ExceptionLogService.Log(ex, \"OutboundDeliveryCoordinator.StatusChanged\"); }", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}

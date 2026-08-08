using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoveryStateServiceTests
{
    [Fact]
    public void UncleanShutdownCreatesRecoveryIncidentAndPendingMarker()
    {
        const string source = "CrashRecoveryStateService.cs";
        var text = File.ReadAllText(Path.Combine("..", "..", "..", "..", "src", "GPTDeskTop", "Services", source));

        Assert.Contains("SetSettingAsync(\"CrashRecoveryPending\", \"1\"", text, StringComparison.Ordinal);
        Assert.Contains("SetSettingAsync(\"CrashRecovery.RecoveryId\", Guid.NewGuid().ToString(\"N\")", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanShutdownIsExplicitlyRecorded()
    {
        const string source = "CrashRecoveryStateService.cs";
        var text = File.ReadAllText(Path.Combine("..", "..", "..", "..", "src", "GPTDeskTop", "Services", source));

        Assert.Contains("MarkCleanShutdownAsync", text, StringComparison.Ordinal);
        Assert.Contains("SetSettingAsync(\"LastShutdownClean\", \"1\"", text, StringComparison.Ordinal);
    }
}

using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class CrashRecoveryStateServiceTests
{
    private static string RepositoryPath(params string[] segments)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(segments)));

    [Fact]
    public void UncleanShutdownCreatesRecoveryIncidentAndPendingMarker()
    {
        var text = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "CrashRecoveryStateService.cs"));

        Assert.Contains("SetSettingAsync(\"CrashRecoveryPending\", \"1\"", text, StringComparison.Ordinal);
        Assert.Contains("SetSettingAsync(\"CrashRecovery.RecoveryId\", Guid.NewGuid().ToString(\"N\")", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanShutdownIsExplicitlyRecorded()
    {
        var text = File.ReadAllText(RepositoryPath("src", "GPTDeskTop", "Services", "CrashRecoveryStateService.cs"));

        Assert.Contains("MarkCleanShutdownAsync", text, StringComparison.Ordinal);
        Assert.Contains("SetSettingAsync(\"LastShutdownClean\", \"1\"", text, StringComparison.Ordinal);
    }
}
using GPTDeskTop.Models;
using GPTDeskTop.Services;

namespace GPTDeskTop.RuntimeTests;

public sealed class SimpleMonitorSingleChromeOwnershipRegressionTests
{
    [Fact]
    public async Task Stable_port_contract_matches_profile_session()
    {
        var root = Path.Combine(Path.GetTempPath(), "gptdesktop-single-chrome", Guid.NewGuid().ToString("N"));
        var profile = new ChromeProfileInfo("Profile 17", "Test", string.Empty, root, Path.Combine(root, "managed"));
        await using var session = new SimpleMonitorProfileSession(profile);
        Assert.Equal(SimpleMonitorChromeOwnershipGate.ResolveStablePort(profile.Key), session.DebuggingPort);
    }

    [Fact]
    public void Physical_send_gate_is_cross_process_and_single_chrome_fail_closed()
    {
        var root = FindRepositoryRoot();
        var safety = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "SimpleMonitorSafetyGate.cs"));
        var ownership = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "SimpleMonitorChromeOwnershipGate.cs"));
        var session = File.ReadAllText(Path.Combine(root, "src", "GPTDeskTop", "Services", "SimpleMonitorProfileSession.cs"));
        Assert.Contains("AcquireGlobalSendLeaseAsync", safety, StringComparison.Ordinal);
        Assert.Contains("EnsureExclusiveBeforeSendAsync", safety, StringComparison.Ordinal);
        Assert.True(safety.IndexOf("EnsureExclusiveBeforeSendAsync", StringComparison.Ordinal) < safety.IndexOf("return new SendPermit", StringComparison.Ordinal));
        Assert.Contains("FileShare.None", ownership, StringComparison.Ordinal);
        Assert.Contains("ChromeProfileCatalog.Discover()", ownership, StringComparison.Ordinal);
        Assert.Contains("gptdesktop-profile-source.txt", ownership, StringComparison.Ordinal);
        Assert.Contains("CloseAllMonitorTabsAsync", ownership, StringComparison.Ordinal);
        Assert.Contains("CloseTabAsync", ownership, StringComparison.Ordinal);
        Assert.Contains("tabs.Count == 1", ownership, StringComparison.Ordinal);
        Assert.Contains("Physical send is blocked", ownership, StringComparison.Ordinal);
        Assert.Contains("SimpleMonitorChromeOwnershipGate.Register", session, StringComparison.Ordinal);
        Assert.Contains("SimpleMonitorChromeOwnershipGate.Unregister", session, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}

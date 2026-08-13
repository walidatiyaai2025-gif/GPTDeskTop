namespace GPTDeskTop.RuntimeTests;

public sealed class OpenConversationAutoDetectionRegressionTests
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
    public void OpenConversationsAreContinuouslyDetectedWithoutBlindGridRefresh()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OpenConversationAutoDetectionExperience.cs");

        Assert.Contains("[ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("DetectionIntervalMs = 1000", source, StringComparison.Ordinal);
        Assert.Contains("_chrome.GetTabsAsync(scanTimeout.Token)", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeHealthPresentation.IsChatGptConversationUrl(tab.Url)", source, StringComparison.Ordinal);
        Assert.Contains("BuildSignature(conversations)", source, StringComparison.Ordinal);

        var equalityGuard = source.IndexOf("string.Equals(_lastConversationSignature, signature", StringComparison.Ordinal);
        var refresh = source.IndexOf("await InvokeRefreshTabsAsync()", StringComparison.Ordinal);
        Assert.True(equalityGuard >= 0 && refresh > equalityGuard);
    }

    [Fact]
    public void AutoDetectionPreservesSelectionAndDoesNotBlankTheListDuringTransientChromeFailure()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OpenConversationAutoDetectionExperience.cs");

        Assert.Contains("CaptureSelectedConversation()", source, StringComparison.Ordinal);
        Assert.Contains("RestoreSelection(selected)", source, StringComparison.Ordinal);
        Assert.Contains("ChatGptConversationIdentity.IsSame(tab.Url, selected.Url)", source, StringComparison.Ordinal);
        Assert.Contains("scanTimeout.CancelAfter(TimeSpan.FromSeconds(2))", source, StringComparison.Ordinal);
        Assert.Contains("ChromeTransportFailureClassifier.IsTransient(ex)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_tabsGrid.DataSource = null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectorIsSingleFlightAndStopsBeforeShutdownCleanup()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "OpenConversationAutoDetectionExperience.cs");

        Assert.Contains("Interlocked.CompareExchange(ref _scanInProgress, 1, 0)", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _scanInProgress, 0)", source, StringComparison.Ordinal);
        Assert.Contains("_form.FormClosing += OnFormClosing", source, StringComparison.Ordinal);
        Assert.Contains("_timer.Stop()", source, StringComparison.Ordinal);
        Assert.Contains("_lifetime.Cancel()", source, StringComparison.Ordinal);
    }
}

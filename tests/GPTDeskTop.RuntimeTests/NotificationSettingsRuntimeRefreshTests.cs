namespace GPTDeskTop.RuntimeTests;

public sealed class NotificationSettingsRuntimeRefreshTests
{
    [Fact]
    public void TrayNotificationReloadBoundaryReadsAllRuntimeNotificationSettings()
    {
        var source = ReadSource("src", "GPTDeskTop", "Services", "TrayNotificationService.cs");

        Assert.Contains("public async Task ReloadSettingsAsync()", source, StringComparison.Ordinal);
        Assert.Contains("GetIntSettingAsync(\"NotificationDurationSeconds\"", source, StringComparison.Ordinal);
        Assert.Contains("GetSettingAsync(\"NotificationSoundEnabled\")", source, StringComparison.Ordinal);
        Assert.Contains("GetSettingAsync(\"NotificationSoundType\")", source, StringComparison.Ordinal);
        Assert.Contains("public async Task InitializeAsync() => await ReloadSettingsAsync();", source, StringComparison.Ordinal);
        Assert.Contains("await ReloadSettingsAsync();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainFormPreservesLegacyConstructorAndAddsNarrowSettingsAppliedCallback()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");

        Assert.Contains("public MainForm(ChromeDevToolsService chrome, ChatGptMonitorService monitor, LocalDatabase database)", source, StringComparison.Ordinal);
        Assert.Contains(": this(chrome, monitor, database, null)", source, StringComparison.Ordinal);
        Assert.Contains("Func<Task>? onSettingsApplied", source, StringComparison.Ordinal);
        Assert.Contains("_onSettingsApplied = onSettingsApplied", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TrayNotificationService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainSettingsInvokesRuntimeRefreshOnlyAfterSuccessfulDialogResult()
    {
        var source = ReadSource("src", "GPTDeskTop", "UI", "MainForm.cs");
        var start = source.IndexOf("private async Task OpenSettingsAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task LaunchChromeAsync()", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        var openSettings = source[start..end];

        var successGuard = openSettings.IndexOf("if (form.ShowDialog(this) != DialogResult.OK) return;", StringComparison.Ordinal);
        var callback = openSettings.IndexOf("await _onSettingsApplied();", StringComparison.Ordinal);
        Assert.True(successGuard >= 0);
        Assert.True(callback > successGuard);
        Assert.Contains("if (_onSettingsApplied is not null)", openSettings, StringComparison.Ordinal);
        Assert.Contains("MainForm.RefreshSettingsRuntime", openSettings, StringComparison.Ordinal);
        Assert.Contains("await RefreshMonitorsAsync()", openSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramWiresTrayNotificationReloadIntoMainSettingsFlow()
    {
        var source = ReadSource("src", "GPTDeskTop", "Program.cs");

        Assert.Contains("using var notifications = new TrayNotificationService(monitor, database);", source, StringComparison.Ordinal);
        Assert.Contains("notifications.InitializeAsync().GetAwaiter().GetResult();", source, StringComparison.Ordinal);
        Assert.Contains("new MainForm(chrome, monitor, database, notifications.ReloadSettingsAsync)", source, StringComparison.Ordinal);
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
